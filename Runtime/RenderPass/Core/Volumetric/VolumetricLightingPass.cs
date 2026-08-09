using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VolumetricLightingPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const int ThreadGroupSizeZ = 4;
        private const string ClearKernelName = "ClearVBufferLighting";
        private const string LightingKernelName = "VolumetricLighting";
        private const string FilterKernelName = "FilterVolumetricLighting";

        private sealed class VolumetricLightingHistoryState : CameraRelativeState
        {
            public VBufferParameters LastVBufferParameters;
            public bool HasLastVBufferParameters;
            public bool HasValidVBufferHistory;
            public int FrameIndex = -1;

            public void ResetHistory()
            {
                LastVBufferParameters = default;
                HasLastVBufferParameters = false;
                HasValidVBufferHistory = false;
                FrameIndex = -1;
            }

            public override void Dispose()
            {
                ResetHistory();
            }
        }

        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepth");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int CSMShadowAtlasId = Shader.PropertyToID("_CSMShadowAtlas");
        private static readonly int CSMViewProjMatricesId = Shader.PropertyToID("_CSMViewProjMatrices");
        private static readonly int CSMCascadeSpheresId = Shader.PropertyToID("_CSMCascadeSpheres");
        private static readonly int CSMAtlasScaleOffsetsId = Shader.PropertyToID("_CSMAtlasScaleOffsets");
        private static readonly int CSMCascadeCountId = Shader.PropertyToID("_CSMCascadeCount");
        private static readonly int CSMMaxShadowDistanceId = Shader.PropertyToID("_CSMMaxShadowDistance");
        private static readonly int CSMNormalBiasId = Shader.PropertyToID("_CSMNormalBias");
        private static readonly int CSMAtlasResolutionId = Shader.PropertyToID("_CSMAtlasResolution");
        private static readonly int CSMCascadeResolutionId = Shader.PropertyToID("_CSMCascadeResolution");
        private static readonly int CSMCascadeWorldTexelSizesId = Shader.PropertyToID("_CSMCascadeWorldTexelSizes");
        private static readonly int CSMCascadeBordersId = Shader.PropertyToID("_CSMCascadeBorders");
        private static readonly int CSMShadowQualityId = Shader.PropertyToID("_CSMShadowQuality");
        private static readonly int VBufferMaxZId = Shader.PropertyToID("_VBufferMaxZ");
        private static readonly int VBufferMaxZEnabledId = Shader.PropertyToID("_VBufferMaxZEnabled");
        private static readonly int VBufferDensityId = Shader.PropertyToID("_VBufferDensity");
        private static readonly int VBufferAnisotropyId = Shader.PropertyToID("_VBufferAnisotropy");
        private static readonly int VBufferHistoryId = Shader.PropertyToID("_VBufferHistory");
        private static readonly int VBufferFeedbackId = Shader.PropertyToID("_VBufferFeedback");
        private static readonly int VBufferLightingId = Shader.PropertyToID("_VBufferLighting");
        private static readonly int VBufferLightingInputId = Shader.PropertyToID("_VBufferLightingInput");
        private static readonly int VBufferLightingOutputId = Shader.PropertyToID("_VBufferLightingOutput");
        private static readonly int VBufferHistoryIsValidId = Shader.PropertyToID("_VBufferHistoryIsValid");
        private static readonly int VBufferSampleOffsetId = Shader.PropertyToID("_VBufferSampleOffset");
        private static readonly int VBufferPrevViewportSizeId = Shader.PropertyToID("_VBufferPrevViewportSize");
        private static readonly int VBufferHistoryViewportScaleId = Shader.PropertyToID("_VBufferHistoryViewportScale");
        private static readonly int VBufferHistoryViewportLimitId = Shader.PropertyToID("_VBufferHistoryViewportLimit");
        private static readonly int VBufferPrevDepthEncodingParamsId = Shader.PropertyToID("_VBufferPrevDepthEncodingParams");
        private static readonly int VBufferPrevDepthDecodingParamsId = Shader.PropertyToID("_VBufferPrevDepthDecodingParams");
        private static readonly int VBufferPrevCameraPositionWSId = Shader.PropertyToID("_VBufferPrevCameraPositionWS");
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int AreaLightCountId = Shader.PropertyToID("_AreaLightCount");
        private static readonly int BigTileLightListId = Shader.PropertyToID("g_vBigTileLightList");
        private static readonly int VolumetricUseBigTileLightListId = Shader.PropertyToID("_VolumetricUseBigTileLightList");
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

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepth;

        [RenderGraphResource(Name = "DirectionalShadowTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DirectionalShadowTexture;

        [RenderGraphResource(Name = "CSMShadowAtlas", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CSMShadowAtlas;

        [RenderGraphResource(Name = "VBufferMaxZ", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferMaxZ;

        [RenderGraphResource(Name = "VBufferDensity", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferDensity;

        [RenderGraphResource(Name = "VBufferAnisotropy", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferAnisotropy;

        [RenderGraphResource(Name = "VBufferLighting", Access = AccessFlags.Write)]
        private RenderGraphTexture m_VBufferLighting;

        [RenderGraphResource(Name = "VBufferLightingFiltered", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_VBufferLightingFiltered;

        private RenderGraphTexture m_VBufferHistory;

        private RenderGraphTexture m_VBufferFeedback;

        [RenderGraphResource(Name = "DirectionalLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(Name = "PunctualLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(Name = "AreaLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(Name = "BigTileLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_BigTileLightListBuffer;

        [RenderGraphResource(Name = "BigTileVolumetricLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_BigTileVolumetricLightListBuffer;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;

        private readonly RenderGraphTexture m_LocalDirectionalShadowTexture;
        private readonly RenderGraphTexture m_LocalCSMShadowAtlas;
        private readonly RenderGraphTexture m_LocalVBufferMaxZ;
        private readonly RenderGraphBuffer m_LocalDirectionalLightBuffer;
        private readonly RenderGraphBuffer m_LocalPunctualLightBuffer;
        private readonly RenderGraphBuffer m_LocalAreaLightBuffer;
        private readonly RenderGraphBuffer m_LocalBigTileLightListBuffer;
        private readonly RenderGraphBuffer m_LocalBigTileVolumetricLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredOffsetBuffer;
        private readonly RenderGraphBuffer m_LocalLayeredLightListBuffer;
        private readonly RenderGraphBuffer m_LocalLogBaseBuffer;

        private ComputeShader m_Shader;
        private int m_ClearKernel = -1;
        private int m_LightingKernel = -1;
        private int m_FilterKernel = -1;
        private int m_DispatchX = 1;
        private int m_DispatchY = 1;
        private int m_DispatchZ = 1;
        private int m_FilterDispatchZ = 1;
        private int m_CameraWidth = 1;
        private int m_CameraHeight = 1;
        private ShaderVariablesVolumetric m_ShaderVariables;
        private VividVolumetricFogSettings m_Settings;
        private int m_DirectionalLightCount;
        private int m_PunctualLightCount;
        private int m_AreaLightCount;
        private int m_MainDirectionalLightIndex = -1;
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
        private bool m_SupportsVolumetricBigTileLightList;
        private bool m_SupportsClusteredPunctualLights;
        private bool m_SupportsClusteredAreaLights;
        private bool m_IsLogBaseBufferEnabled;
        private RenderGraphBuffer m_FrameDataBigTileVolumetricLightListBuffer;
        private readonly RenderGraphTextureDesc m_VBufferHistoryDescriptor = new();
        private readonly CameraRelativeSystem<VolumetricLightingHistoryState> m_HistoryStates = new();
        private CameraHistoryTexture m_VBufferLightingHistory;
        private VolumetricLightingHistoryState m_CurrentHistoryState;
        private VBufferParameters m_PreviousVBufferParameters;
        private VBufferParameters m_LastVBufferParameters;
        private bool m_HasLastVBufferParameters;
        private bool m_HasValidVBufferHistory;
        private bool m_IsFirstFrame = true;
        private Vector3 m_PreviousCameraPositionWS;
        private Vector4 m_VBufferSampleOffset;
        private readonly Matrix4x4[] m_CSMViewProjMatrices = new Matrix4x4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CSMCascadeSpheres = new Vector4[VividShadowData.MaxCascadeCount];
        private readonly Vector4[] m_CSMAtlasScaleOffsets = new Vector4[VividShadowData.MaxCascadeCount];
        private Vector4 m_CSMCascadeWorldTexelSizes = Vector4.zero;
        private Vector4 m_CSMCascadeBorders = Vector4.zero;
        private int m_CSMCascadeCount;
        private float m_CSMMaxShadowDistance;
        private float m_CSMNormalBias;
        private int m_CSMAtlasResolution;
        private int m_CSMCascadeResolution;
        private int m_CSMShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;

        public VolumetricLightingPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VolumetricLightingPass));
            m_CameraDepth = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_CameraDepth.desc.FilterMode = FilterMode.Point;
            m_LocalDirectionalShadowTexture = RenderGraphTexture.CreateColorTarget("DirectionalShadowTexture", GraphicsFormat.R16_SFloat);
            m_LocalDirectionalShadowTexture.desc.ClearBuffer = true;
            m_LocalDirectionalShadowTexture.desc.ClearColor = Color.white;
            m_LocalDirectionalShadowTexture.desc.FilterMode = FilterMode.Point;
            m_DirectionalShadowTexture = m_LocalDirectionalShadowTexture;
            m_LocalCSMShadowAtlas = RenderGraphTexture.CreateInput("CSMShadowAtlas", GraphicsFormat.None, DepthBits.Depth16);
            m_LocalCSMShadowAtlas.desc.IsShadowMap = true;
            m_CSMShadowAtlas = m_LocalCSMShadowAtlas;
            m_LocalVBufferMaxZ = VolumetricMaxZPass.CreateVBufferMaxZTexture("VBufferMaxZ");
            m_VBufferMaxZ = m_LocalVBufferMaxZ;
            m_VBufferDensity = VolumetricDensityPass.CreateVBufferTexture("VBufferDensity");
            m_VBufferAnisotropy = VolumetricDensityPass.CreateVBufferScalarTexture("VBufferAnisotropy");
            m_VBufferLighting = VolumetricDensityPass.CreateVBufferTexture("VBufferLighting");
            m_VBufferLighting.desc.ClearColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            m_VBufferLightingFiltered = VolumetricDensityPass.CreateVBufferTexture("VBufferLightingFiltered");
            m_VBufferHistory = VolumetricDensityPass.CreateVBufferTexture("VBufferHistory");
            m_VBufferHistory.desc.ClearBuffer = false;
            m_VBufferFeedback = VolumetricDensityPass.CreateVBufferTexture("VBufferFeedback");
            m_VBufferFeedback.desc.ClearBuffer = false;
            m_LocalDirectionalLightBuffer = RenderGraphBuffer.CreateStructured("DirectionalLights", 1, VividLightData.DirectionalLightData.Stride);
            m_LocalPunctualLightBuffer = RenderGraphBuffer.CreateStructured("PunctualLights", 1, VividLightData.PunctualLightData.Stride);
            m_LocalAreaLightBuffer = RenderGraphBuffer.CreateStructured("AreaLights", 1, VividLightData.AreaLightData.Stride);
            m_LocalBigTileLightListBuffer = RenderGraphBuffer.CreateStructured("BigTileLightList", 1, sizeof(uint));
            m_LocalBigTileVolumetricLightListBuffer = RenderGraphBuffer.CreateStructured("BigTileVolumetricLightList", 1, sizeof(uint));
            m_LocalLayeredOffsetBuffer = RenderGraphBuffer.CreateStructured("LayeredOffset", 1, sizeof(uint));
            m_LocalLayeredLightListBuffer = RenderGraphBuffer.CreateStructured("LayeredLightList", 1, sizeof(uint));
            m_LocalLogBaseBuffer = RenderGraphBuffer.CreateStructured("LogBaseBuffer", 1, sizeof(float));
            m_DirectionalLightBuffer = m_LocalDirectionalLightBuffer;
            m_PunctualLightBuffer = m_LocalPunctualLightBuffer;
            m_AreaLightBuffer = m_LocalAreaLightBuffer;
            m_BigTileLightListBuffer = m_LocalBigTileLightListBuffer;
            m_BigTileVolumetricLightListBuffer = m_LocalBigTileVolumetricLightListBuffer;
            m_LayeredOffsetBuffer = m_LocalLayeredOffsetBuffer;
            m_LayeredLightListBuffer = m_LocalLayeredLightListBuffer;
            m_LogBaseBuffer = m_LocalLogBaseBuffer;
        }

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.VolumetricLightingCompute;
            if (m_Shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find volumetric lighting compute shader for {nameof(VolumetricLightingPass)}.");
                return;
            }

            m_ClearKernel = m_Shader.FindKernel(ClearKernelName);
            m_LightingKernel = m_Shader.FindKernel(LightingKernelName);
            m_FilterKernel = m_Shader.FindKernel(FilterKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_CameraWidth = CameraDimensionUtility.ResolveCameraDimension(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width);
            m_CameraHeight = CameraDimensionUtility.ResolveCameraDimension(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height);

            var volumetricData = frameData.GetOrCreate<VividVolumetricData>();
            m_Settings = volumetricData.VBufferDensity != null
                ? volumetricData.settings
                : VividVolumetricUtility.ResolveSettings(frameData);
            m_ShaderVariables = volumetricData.VBufferDensity != null
                ? volumetricData.shaderVariables
                : VividVolumetricUtility.BuildShaderVariables(m_Settings, m_CameraWidth, m_CameraHeight, 0, cameraData);
            var temporalData = frameData.Get<VividTemporalData>();
            m_CurrentHistoryState = ResolveHistoryState(cameraData.camera);
            m_HasLastVBufferParameters = m_CurrentHistoryState?.HasLastVBufferParameters ?? false;
            m_HasValidVBufferHistory = m_CurrentHistoryState?.HasValidVBufferHistory ?? false;
            m_LastVBufferParameters = m_CurrentHistoryState?.LastVBufferParameters ?? default;
            m_IsFirstFrame = temporalData == null || temporalData.isFirstFrame;
            m_PreviousCameraPositionWS = ResolvePreviousCameraPositionWS(cameraData, temporalData);
            m_PreviousVBufferParameters = m_HasLastVBufferParameters
                ? m_LastVBufferParameters
                : m_Settings.VBufferParameters;
            m_VBufferSampleOffset = m_Settings.TemporalReprojectionEnabled
                ? ComputeVBufferSampleOffset(ResolveVolumetricFrameIndex(cameraData))
                : Vector4.zero;

            ConfigureCameraDepthTexture(m_CameraWidth, m_CameraHeight);
            if (ReferenceEquals(m_VBufferMaxZ, m_LocalVBufferMaxZ))
            {
                var localMaxZWidth = Mathf.Max(1, CoreUtils.DivRoundUp(
                    CoreUtils.DivRoundUp(m_CameraWidth, VolumetricMaxZPass.MaxZTileSize),
                    VolumetricMaxZPass.FinalMaskDownsample));
                var localMaxZHeight = Mathf.Max(1, CoreUtils.DivRoundUp(
                    CoreUtils.DivRoundUp(m_CameraHeight, VolumetricMaxZPass.MaxZTileSize),
                    VolumetricMaxZPass.FinalMaskDownsample));
                VolumetricMaxZPass.ConfigureVBufferMaxZTexture(m_VBufferMaxZ, localMaxZWidth, localMaxZHeight, "VBufferMaxZ");
            }
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferDensity, m_Settings.VBufferParameters, "VBufferDensity", clear: false);
            VolumetricDensityPass.ConfigureVBufferScalarTexture(m_VBufferAnisotropy, m_Settings.VBufferParameters, "VBufferAnisotropy", clear: false);
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferLighting, m_Settings.VBufferParameters, "VBufferLighting", clear: true);
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferLightingFiltered, m_Settings.VBufferParameters, "VBufferLightingFiltered", clear: false);
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferHistory, m_Settings.VBufferParameters, "VBufferHistory", clear: false);
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferFeedback, m_Settings.VBufferParameters, "VBufferFeedback", clear: false);
            m_DispatchX = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportWidth, ThreadGroupSizeX);
            m_DispatchY = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportHeight, ThreadGroupSizeY);
            m_DispatchZ = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.SliceCount, ThreadGroupSizeZ);
            m_FilterDispatchZ = Mathf.Max(m_Settings.VBufferParameters.SliceCount, 1);
            PrepareClusteredLightingParameters(frameData);
            PrepareDirectionalShadowParameters(frameData);
            PrepareVBufferHistory(cameraData.camera);

            volumetricData.settings = m_Settings;
            volumetricData.shaderVariables = m_ShaderVariables;
            volumetricData.VBufferLighting = m_VBufferLighting;
            volumetricData.enabled = m_Settings.Enabled;
            volumetricData.gaussianFilteringEnabled = m_Settings.GaussianFilteringEnabled;
        }

        public override void Record(ComputePassContext context)
        {
            if (!CanExecute())
                return;

            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                ConstantBuffer.Push(cmd, m_ShaderVariables, m_Shader, ShaderVariablesVolumetricId);
                cmd.SetComputeTextureParam(m_Shader, m_ClearKernel, VBufferLightingId, m_VBufferLighting.innerHandle);
                cmd.DispatchCompute(m_Shader, m_ClearKernel, m_DispatchX, m_DispatchY, m_DispatchZ);

                if (!m_Settings.Enabled || m_VBufferDensity?.innerHandle.IsValid() != true || m_VBufferAnisotropy?.innerHandle.IsValid() != true)
                    return;
                if (m_VBufferFeedback?.innerHandle.IsValid() != true)
                    return;

                m_VBufferLightingHistory?.MarkWritten();
                var lightingTarget = m_Settings.GaussianFilteringEnabled ? m_VBufferLightingFiltered : m_VBufferLighting;
                BindSharedTextures(context, cmd, m_LightingKernel, lightingTarget);
                BindLightLoopParameters(cmd, m_LightingKernel);
                cmd.DispatchCompute(m_Shader, m_LightingKernel, m_DispatchX, m_DispatchY, 1);

                if (m_Settings.GaussianFilteringEnabled)
                {
                    cmd.SetComputeTextureParam(m_Shader, m_FilterKernel, VBufferLightingInputId, m_VBufferLightingFiltered.innerHandle);
                    cmd.SetComputeTextureParam(m_Shader, m_FilterKernel, VBufferLightingOutputId, m_VBufferLighting.innerHandle);
                    cmd.DispatchCompute(m_Shader, m_FilterKernel, m_DispatchX, m_DispatchY, m_FilterDispatchZ);
                }
            }
        }

        public override void Dispose()
        {
            m_Shader = null;
            m_ClearKernel = -1;
            m_LightingKernel = -1;
            m_FilterKernel = -1;
            m_Settings = default;
            m_ShaderVariables = default;
            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_FilterDispatchZ = 1;
            m_SupportsVolumetricBigTileLightList = false;
            m_FrameDataBigTileVolumetricLightListBuffer = null;
            m_CSMCascadeCount = 0;
            m_CSMCascadeWorldTexelSizes = Vector4.zero;
            m_CSMCascadeBorders = Vector4.zero;
            m_HasValidVBufferHistory = false;
            m_HasLastVBufferParameters = false;
            m_IsFirstFrame = true;
            m_VBufferLightingHistory = null;
            m_CurrentHistoryState = null;
            m_HistoryStates.Dispose();
        }

        private void PrepareDirectionalShadowParameters(ContextContainer frameData)
        {
            m_CSMCascadeCount = 0;
            m_CSMMaxShadowDistance = 0.0f;
            m_CSMNormalBias = 0.0f;
            m_CSMAtlasResolution = 0;
            m_CSMCascadeResolution = 0;
            m_CSMCascadeWorldTexelSizes = Vector4.zero;
            m_CSMCascadeBorders = Vector4.zero;
            m_CSMShadowQuality = (int)VividAdditionalLightData.CSMScreenSpaceShadowQuality.Low;

            for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
            {
                m_CSMViewProjMatrices[i] = Matrix4x4.identity;
                m_CSMCascadeSpheres[i] = Vector4.zero;
                m_CSMAtlasScaleOffsets[i] = Vector4.zero;
            }

            var shadowData = frameData.GetOrCreate<VividShadowData>();
            if (!shadowData.isCSMActive)
                return;

            m_CSMCascadeCount = Mathf.Clamp(shadowData.cascadeCount, 0, VividShadowData.MaxCascadeCount);
            m_CSMMaxShadowDistance = Mathf.Max(shadowData.maxShadowDistance, 0.0f);
            m_CSMNormalBias = Mathf.Max(shadowData.normalBias, 0.0f);
            m_CSMAtlasResolution = Mathf.Max(shadowData.atlasResolution, 1);
            m_CSMCascadeResolution = Mathf.Max(shadowData.cascadeResolution, 1);

            for (int i = 0; i < VividShadowData.MaxCascadeCount; i++)
            {
                m_CSMViewProjMatrices[i] = shadowData.viewProjMatrices[i];
                m_CSMCascadeSpheres[i] = shadowData.cascadeSpheres[i];
                m_CSMAtlasScaleOffsets[i] = shadowData.cascadeAtlasScaleOffsets[i];
                m_CSMCascadeWorldTexelSizes[i] = shadowData.cascadeWorldTexelSizes[i];
                m_CSMCascadeBorders[i] = shadowData.cascadeBorders[i];
            }

            var lightData = frameData.GetOrCreate<VividLightData>();
            if (DirectionalRayTracedShadowPass.TryResolveMainDirectionalLight(lightData, out _, out var additionalLightData)
                && additionalLightData != null)
            {
                m_CSMShadowQuality = (int)additionalLightData.screenSpaceShadowQuality;
            }
        }

        private void BindSharedTextures(ComputePassContext context, ComputeCommandBuffer cmd, int kernel, RenderGraphTexture lightingTarget)
        {
            cmd.SetComputeTextureParam(m_Shader, kernel, CameraDepthId, m_CameraDepth.innerHandle);
            BindVBufferMaxZ(context, cmd, kernel);
            cmd.SetComputeTextureParam(m_Shader, kernel, VBufferDensityId, m_VBufferDensity.innerHandle);
            cmd.SetComputeTextureParam(m_Shader, kernel, VBufferAnisotropyId, m_VBufferAnisotropy.innerHandle);
            BindVBufferHistory(cmd, kernel);
            cmd.SetComputeTextureParam(m_Shader, kernel, VBufferFeedbackId, m_VBufferFeedback.innerHandle);
            cmd.SetComputeTextureParam(m_Shader, kernel, VBufferLightingId, lightingTarget.innerHandle);

            if (ReferenceEquals(m_DirectionalShadowTexture, m_LocalDirectionalShadowTexture)
                || m_DirectionalShadowTexture == null
                || !m_DirectionalShadowTexture.innerHandle.IsValid())
            {
                cmd.SetComputeTextureParam(
                    m_Shader,
                    kernel,
                    DirectionalShadowTextureId,
                    context.renderGraphContext.defaultResources.whiteTexture);
            }
            else
            {
                cmd.SetComputeTextureParam(m_Shader, kernel, DirectionalShadowTextureId, m_DirectionalShadowTexture.innerHandle);
            }

            BindDirectionalShadowParameters(context, cmd, kernel);
        }

        private void BindDirectionalShadowParameters(ComputePassContext context, ComputeCommandBuffer cmd, int kernel)
        {
            var hasCSMShadowAtlas = !ReferenceEquals(m_CSMShadowAtlas, m_LocalCSMShadowAtlas)
                && m_CSMShadowAtlas?.innerHandle.IsValid() == true
                && m_CSMCascadeCount > 0;

            cmd.SetComputeTextureParam(
                m_Shader,
                kernel,
                CSMShadowAtlasId,
                hasCSMShadowAtlas
                    ? m_CSMShadowAtlas.innerHandle
                    : context.renderGraphContext.defaultResources.blackTexture);
            cmd.SetComputeMatrixArrayParam(m_Shader, CSMViewProjMatricesId, m_CSMViewProjMatrices);
            cmd.SetComputeVectorArrayParam(m_Shader, CSMCascadeSpheresId, m_CSMCascadeSpheres);
            cmd.SetComputeVectorArrayParam(m_Shader, CSMAtlasScaleOffsetsId, m_CSMAtlasScaleOffsets);
            cmd.SetComputeIntParam(m_Shader, CSMCascadeCountId, hasCSMShadowAtlas ? m_CSMCascadeCount : 0);
            cmd.SetComputeFloatParam(m_Shader, CSMMaxShadowDistanceId, m_CSMMaxShadowDistance);
            cmd.SetComputeFloatParam(m_Shader, CSMNormalBiasId, m_CSMNormalBias);
            cmd.SetComputeIntParam(m_Shader, CSMAtlasResolutionId, m_CSMAtlasResolution);
            cmd.SetComputeIntParam(m_Shader, CSMCascadeResolutionId, m_CSMCascadeResolution);
            cmd.SetComputeVectorParam(m_Shader, CSMCascadeWorldTexelSizesId, m_CSMCascadeWorldTexelSizes);
            cmd.SetComputeVectorParam(m_Shader, CSMCascadeBordersId, m_CSMCascadeBorders);
            cmd.SetComputeIntParam(m_Shader, CSMShadowQualityId, CSMShadowResolvePass.ResolveCSMFilteringQuality(m_CSMShadowQuality));
        }

        private void BindVBufferHistory(ComputeCommandBuffer cmd, int kernel)
        {
            var hasValidHistory = m_Settings.TemporalReprojectionEnabled
                && m_HasValidVBufferHistory
                && !m_IsFirstFrame;
            var historyTexture = m_VBufferHistory?.innerHandle.IsValid() == true
                ? m_VBufferHistory.innerHandle
                : m_VBufferDensity.innerHandle;
            var previousParameters = m_PreviousVBufferParameters;
            var previousViewportSize = new Vector4(
                previousParameters.ViewportWidth,
                previousParameters.ViewportHeight,
                previousParameters.RcpViewportWidth,
                previousParameters.RcpViewportHeight);
            var historyViewportScale = ComputeHistoryViewportScale(previousParameters);
            var historyViewportLimit = ComputeHistoryViewportLimit(previousParameters);

            cmd.SetComputeTextureParam(m_Shader, kernel, VBufferHistoryId, historyTexture);
            cmd.SetComputeIntParam(m_Shader, VBufferHistoryIsValidId, hasValidHistory ? 1 : 0);
            cmd.SetComputeVectorParam(m_Shader, VBufferSampleOffsetId, m_VBufferSampleOffset);
            cmd.SetComputeVectorParam(m_Shader, VBufferPrevViewportSizeId, previousViewportSize);
            cmd.SetComputeVectorParam(m_Shader, VBufferHistoryViewportScaleId, historyViewportScale);
            cmd.SetComputeVectorParam(m_Shader, VBufferHistoryViewportLimitId, historyViewportLimit);
            cmd.SetComputeVectorParam(m_Shader, VBufferPrevDepthEncodingParamsId, previousParameters.DepthEncodingParams);
            cmd.SetComputeVectorParam(m_Shader, VBufferPrevDepthDecodingParamsId, previousParameters.DepthDecodingParams);
            cmd.SetComputeVectorParam(
                m_Shader,
                VBufferPrevCameraPositionWSId,
                new Vector4(m_PreviousCameraPositionWS.x, m_PreviousCameraPositionWS.y, m_PreviousCameraPositionWS.z, 1.0f));
        }

        private void BindVBufferMaxZ(ComputePassContext context, ComputeCommandBuffer cmd, int kernel)
        {
            var hasVBufferMaxZ = !ReferenceEquals(m_VBufferMaxZ, m_LocalVBufferMaxZ)
                && m_VBufferMaxZ?.innerHandle.IsValid() == true;
            cmd.SetComputeFloatParam(m_Shader, VBufferMaxZEnabledId, hasVBufferMaxZ ? 1.0f : 0.0f);
            cmd.SetComputeTextureParam(
                m_Shader,
                kernel,
                VBufferMaxZId,
                hasVBufferMaxZ
                    ? m_VBufferMaxZ.innerHandle
                    : context.renderGraphContext.defaultResources.blackTexture);
        }

        private void BindLightLoopParameters(ComputeCommandBuffer cmd, int kernel)
        {
            cmd.SetComputeIntParam(m_Shader, DirectionalLightCountId, m_DirectionalLightCount);
            cmd.SetComputeIntParam(m_Shader, MainDirectionalLightIndexId, m_MainDirectionalLightIndex);
            cmd.SetComputeIntParam(m_Shader, AreaLightCountId, m_AreaLightCount);
            cmd.SetComputeIntParam(m_Shader, VolumetricUseBigTileLightListId, m_SupportsVolumetricBigTileLightList ? 1 : 0);
            cmd.SetComputeIntParam(m_Shader, ClusteredPunctualLightGridEnabledId, m_SupportsClusteredPunctualLights ? 1 : 0);
            cmd.SetComputeIntParam(m_Shader, ClusteredAreaLightGridEnabledId, m_SupportsClusteredAreaLights ? 1 : 0);
            cmd.SetComputeIntParam(m_Shader, ClusteredReflectionProbeGridEnabledId, 0);
            cmd.SetComputeIntParam(m_Shader, ClusteredDecalGridEnabledId, 0);
            cmd.SetComputeIntParam(m_Shader, ClusterTileSizeId, m_ClusterTileSize);
            cmd.SetComputeIntParam(m_Shader, BigTileSizeId, LightGridPass.ClusterBigTileSize);
            cmd.SetComputeIntParam(m_Shader, ClusterSliceCountId, m_ClusterSliceCount);
            cmd.SetComputeIntParam(m_Shader, ClusterTileCountXId, m_ClusterTileCountX);
            cmd.SetComputeIntParam(m_Shader, ClusterTileCountYId, m_ClusterTileCountY);
            cmd.SetComputeIntParam(m_Shader, ClusterIsOrthographicId, m_ClusterIsOrthographic);
            cmd.SetComputeFloatParam(m_Shader, ClusterNearClipId, m_ClusterNearClip);
            cmd.SetComputeFloatParam(m_Shader, ClusterFarClipId, m_ClusterFarClip);
            cmd.SetComputeFloatParam(m_Shader, ClusterScaleId, m_ClusterScale);
            cmd.SetComputeFloatParam(m_Shader, ClusterBaseId, m_ClusterBase);
            cmd.SetComputeFloatParam(m_Shader, NearPlaneId, m_ClusterNearClip);
            cmd.SetComputeFloatParam(m_Shader, FarPlaneId, m_ClusterFarClip);
            cmd.SetComputeIntParam(m_Shader, Log2NumClustersId, m_ClusterLog2SliceCount);
            cmd.SetComputeIntParam(m_Shader, IsLogBaseBufferEnabledId, m_IsLogBaseBufferEnabled ? 1 : 0);
            cmd.SetComputeIntParam(m_Shader, NumTileClusteredXId, m_ClusterTileCountX);
            cmd.SetComputeIntParam(m_Shader, NumTileClusteredYId, m_ClusterTileCountY);
            cmd.SetComputeIntParam(m_Shader, NumTileBigTileXId, m_BigTileCountX);
            cmd.SetComputeIntParam(m_Shader, NumTileBigTileYId, m_BigTileCountY);

            SetLightLoopBuffer(cmd, kernel, DirectionalLightsId, m_DirectionalLightBuffer);
            SetLightLoopBuffer(cmd, kernel, PunctualLightsId, m_PunctualLightBuffer);
            SetLightLoopBuffer(cmd, kernel, AreaLightsId, m_AreaLightBuffer);
            SetLightLoopBuffer(cmd, kernel, BigTileLightListId, GetBigTileVolumetricLightListBufferForBinding());
            SetLightLoopBuffer(cmd, kernel, LayeredOffsetId, m_LayeredOffsetBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredLightListId, m_LayeredLightListBuffer);
            SetLightLoopBuffer(cmd, kernel, LogBaseBufferId, m_LogBaseBuffer);
        }

        private void PrepareVBufferHistory(Camera camera)
        {
            m_VBufferLightingHistory = null;
            if (!m_Settings.Enabled || !m_Settings.TemporalReprojectionEnabled)
            {
                m_HasValidVBufferHistory = false;
                m_HasLastVBufferParameters = false;
                m_CurrentHistoryState?.ResetHistory();
                return;
            }

            var hadLastVBufferParameters = m_HasLastVBufferParameters;
            var historyParametersCompatible = hadLastVBufferParameters
                && AreVBufferParametersCompatible(m_LastVBufferParameters, m_Settings.VBufferParameters);
            if (camera == null)
            {
                m_HasValidVBufferHistory = false;
                return;
            }

            var history = camera.GetVividCameraHistory();
            var descriptor = CameraHistoryRenderGraphBridge.CreateDescriptor(CreateVBufferHistoryDescriptor());
            m_VBufferLightingHistory = history.GetOrCreateTexture(
                CameraHistoryIds.VolumetricLighting,
                2,
                descriptor);
            CameraHistoryRenderGraphBridge.BindForPass(
                this,
                m_VBufferHistory,
                m_VBufferLightingHistory,
                1,
                AccessFlags.Read);
            CameraHistoryRenderGraphBridge.BindForPass(
                this,
                m_VBufferFeedback,
                m_VBufferLightingHistory,
                0,
                AccessFlags.ReadWrite);
            m_HasValidVBufferHistory = m_VBufferLightingHistory.IsValid()
                && historyParametersCompatible
                && !m_IsFirstFrame;

            m_LastVBufferParameters = m_Settings.VBufferParameters;
            m_HasLastVBufferParameters = true;
            if (m_CurrentHistoryState != null)
            {
                m_CurrentHistoryState.LastVBufferParameters = m_LastVBufferParameters;
                m_CurrentHistoryState.HasLastVBufferParameters = m_HasLastVBufferParameters;
                m_CurrentHistoryState.HasValidVBufferHistory = m_HasValidVBufferHistory;
            }
        }

        internal static bool AreVBufferParametersCompatible(
            in VBufferParameters previousParameters,
            in VBufferParameters currentParameters)
        {
            return previousParameters.ViewportWidth == currentParameters.ViewportWidth
                && previousParameters.ViewportHeight == currentParameters.ViewportHeight
                && previousParameters.SliceCount == currentParameters.SliceCount
                && Approximately(previousParameters.ScreenPercentage, currentParameters.ScreenPercentage)
                && Approximately(previousParameters.DepthExtent, currentParameters.DepthExtent)
                && Approximately(previousParameters.SliceDistributionUniformity, currentParameters.SliceDistributionUniformity)
                // Raw camera clip planes are intentionally omitted because VBuffer history compatibility
                // is driven by the actual sampling space below.
                && Approximately(previousParameters.VerticalFoVRadians, currentParameters.VerticalFoVRadians)
                && Approximately(previousParameters.LastSliceDistance, currentParameters.LastSliceDistance)
                && Approximately(previousParameters.UnitDepthTexelSpacing, currentParameters.UnitDepthTexelSpacing)
                && Approximately(previousParameters.DepthEncodingParams, currentParameters.DepthEncodingParams)
                && Approximately(previousParameters.DepthDecodingParams, currentParameters.DepthDecodingParams);
        }

        private static bool Approximately(Vector4 lhs, Vector4 rhs)
        {
            return Approximately(lhs.x, rhs.x)
                && Approximately(lhs.y, rhs.y)
                && Approximately(lhs.z, rhs.z)
                && Approximately(lhs.w, rhs.w);
        }

        private static bool Approximately(float lhs, float rhs)
        {
            return Mathf.Abs(lhs - rhs) <= 0.0001f * Mathf.Max(1.0f, Mathf.Max(Mathf.Abs(lhs), Mathf.Abs(rhs)));
        }

        private VolumetricLightingHistoryState ResolveHistoryState(Camera camera)
        {
            m_HistoryStates.PurgeDestroyedCameras();
            return camera != null ? m_HistoryStates.GetOrCreateBase(camera) : null;
        }

        private int ResolveVolumetricFrameIndex(VividCameraData cameraData)
        {
            if (m_CurrentHistoryState == null)
                return ResolveFrameIndex(cameraData);

            unchecked
            {
                m_CurrentHistoryState.FrameIndex++;
            }

            if (m_CurrentHistoryState.FrameIndex < 0)
                m_CurrentHistoryState.FrameIndex = 0;

            return m_CurrentHistoryState.FrameIndex;
        }

        private RenderGraphTextureDesc CreateVBufferHistoryDescriptor()
        {
            var desc = m_VBufferHistoryDescriptor;
            if (m_VBufferFeedback?.desc != null)
                m_VBufferFeedback.desc.Copy(desc);

            desc.Name = "VBufferFeedback";
            desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            desc.DepthBufferBits = DepthBits.None;
            desc.MsaaSamples = MSAASamples.None;
            desc.Dimension = TextureDimension.Tex3D;
            desc.ClearBuffer = false;
            desc.EnableRandomWrite = true;
            desc.FilterMode = FilterMode.Bilinear;
            desc.WrapMode = TextureWrapMode.Clamp;
            desc.UseMipMap = false;
            desc.AutoGenerateMips = false;
            desc.MipCount = 1;
            desc.BindTextureMS = false;
            return desc;
        }

        private Vector4 ComputeHistoryViewportScale(in VBufferParameters previousParameters)
        {
            var desc = m_VBufferHistory?.desc ?? m_VBufferFeedback?.desc;
            var bufferWidth = Mathf.Max(desc?.Width ?? previousParameters.ViewportWidth, 1);
            var bufferHeight = Mathf.Max(desc?.Height ?? previousParameters.ViewportHeight, 1);
            var bufferSlices = Mathf.Max(desc?.Slices ?? previousParameters.SliceCount, 1);
            return new Vector4(
                VividVolumetricUtility.ComputeViewportScale(previousParameters.ViewportWidth, bufferWidth),
                VividVolumetricUtility.ComputeViewportScale(previousParameters.ViewportHeight, bufferHeight),
                VividVolumetricUtility.ComputeViewportScale(previousParameters.SliceCount, bufferSlices),
                0.0f);
        }

        private Vector4 ComputeHistoryViewportLimit(in VBufferParameters previousParameters)
        {
            var desc = m_VBufferHistory?.desc ?? m_VBufferFeedback?.desc;
            var bufferWidth = Mathf.Max(desc?.Width ?? previousParameters.ViewportWidth, 1);
            var bufferHeight = Mathf.Max(desc?.Height ?? previousParameters.ViewportHeight, 1);
            var bufferSlices = Mathf.Max(desc?.Slices ?? previousParameters.SliceCount, 1);
            return new Vector4(
                VividVolumetricUtility.ComputeViewportLimit(previousParameters.ViewportWidth, bufferWidth),
                VividVolumetricUtility.ComputeViewportLimit(previousParameters.ViewportHeight, bufferHeight),
                VividVolumetricUtility.ComputeViewportLimit(previousParameters.SliceCount, bufferSlices),
                0.0f);
        }

        private static Vector3 ResolvePreviousCameraPositionWS(VividCameraData cameraData, VividTemporalData temporalData)
        {
            if (temporalData != null && !temporalData.isFirstFrame)
            {
                var previousCameraToWorld = temporalData.previousViewMatrix.inverse;
                var position = previousCameraToWorld.GetColumn(3);
                return new Vector3(position.x, position.y, position.z);
            }

            var camera = cameraData?.camera;
            return camera != null ? camera.transform.position : Vector3.zero;
        }

        private static int ResolveFrameIndex(VividCameraData cameraData)
        {
            return cameraData != null && cameraData.frameIndex >= 0
                ? cameraData.frameIndex
                : Time.frameCount;
        }

        private static Vector4 ComputeVBufferSampleOffset(int frameIndex)
        {
            var sampleIndex = frameIndex % 7;
            if (sampleIndex < 0)
                sampleIndex += 7;

            const float r = 0.17054068870105444f;
            var d = 2.0f * r;
            var s = r * Mathf.Sqrt(3.0f);
            var sample = Vector2.zero;
            switch (sampleIndex)
            {
                case 1:
                    sample = new Vector2(-d, 0.0f);
                    break;
                case 2:
                    sample = new Vector2(d, 0.0f);
                    break;
                case 3:
                    sample = new Vector2(-r, -s);
                    break;
                case 4:
                    sample = new Vector2(r, s);
                    break;
                case 5:
                    sample = new Vector2(r, -s);
                    break;
                case 6:
                    sample = new Vector2(-r, s);
                    break;
            }

            const float cos15 = 0.9659258262890683f;
            const float sin15 = 0.25881904510252074f;
            var rotated = new Vector2(
                sample.x * cos15 - sample.y * sin15,
                sample.x * sin15 + sample.y * cos15);
            return new Vector4(rotated.x, rotated.y, ResolveVBufferZSampleOffset(sampleIndex), frameIndex);
        }

        private static float ResolveVBufferZSampleOffset(int sampleIndex)
        {
            switch (sampleIndex)
            {
                case 1:
                    return 3.0f / 14.0f;
                case 2:
                    return 11.0f / 14.0f;
                case 3:
                    return 5.0f / 14.0f;
                case 4:
                    return 9.0f / 14.0f;
                case 5:
                    return 1.0f / 14.0f;
                case 6:
                    return 13.0f / 14.0f;
                default:
                    return 7.0f / 14.0f;
            }
        }

        private void PrepareClusteredLightingParameters(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = 0;
            m_PunctualLightCount = 0;
            m_AreaLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_CameraWidth / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_CameraHeight / (float)m_ClusterTileSize));
            m_BigTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_CameraWidth / (float)LightGridPass.ClusterBigTileSize));
            m_BigTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_CameraHeight / (float)LightGridPass.ClusterBigTileSize));
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsVolumetricBigTileLightList = false;
            m_SupportsClusteredPunctualLights = false;
            m_SupportsClusteredAreaLights = false;
            m_IsLogBaseBufferEnabled = false;
            m_FrameDataBigTileVolumetricLightListBuffer = null;

            if (clusteredLightingData == null)
                return;

            m_FrameDataBigTileVolumetricLightListBuffer = clusteredLightingData.bigTileVolumetricLightList;
            m_MainDirectionalLightIndex = clusteredLightingData.mainDirectionalLightIndex;
            m_ClusterTileSize = clusteredLightingData.clusterTileSize > 0 ? clusteredLightingData.clusterTileSize : m_ClusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount > 0 ? clusteredLightingData.clusterSliceCount : m_ClusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX > 0 ? clusteredLightingData.clusterTileCountX : m_ClusterTileCountX;
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY > 0 ? clusteredLightingData.clusterTileCountY : m_ClusterTileCountY;
            m_BigTileCountX = clusteredLightingData.bigTileCountX > 0 ? clusteredLightingData.bigTileCountX : m_BigTileCountX;
            m_BigTileCountY = clusteredLightingData.bigTileCountY > 0 ? clusteredLightingData.bigTileCountY : m_BigTileCountY;
            m_ClusterNearClip = clusteredLightingData.clusterNearClip > 0.0f ? clusteredLightingData.clusterNearClip : m_ClusterNearClip;
            m_ClusterFarClip = clusteredLightingData.clusterFarClip > m_ClusterNearClip ? clusteredLightingData.clusterFarClip : m_ClusterFarClip;
            m_ClusterIsOrthographic = clusteredLightingData.clusterIsOrthographic;
            m_ClusterScale = clusteredLightingData.clusterScale;
            m_ClusterBase = clusteredLightingData.clusterBase > 0.0f ? clusteredLightingData.clusterBase : LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = clusteredLightingData.clusterLog2SliceCount > 0
                ? clusteredLightingData.clusterLog2SliceCount
                : LightGridPass.ClusterLog2SliceCount;

            if (HasBoundDirectionalLightBuffer())
                m_DirectionalLightCount = Mathf.Max(0, clusteredLightingData.directionalLightCount);
            else
                m_MainDirectionalLightIndex = -1;

            var supportsClusteredFiniteLights = clusteredLightingData.supportsClusteredPunctualLights;
            m_PunctualLightCount = HasBoundPunctualLightBuffer()
                ? Mathf.Max(0, clusteredLightingData.punctualLightCount)
                : 0;
            m_AreaLightCount = HasBoundAreaLightBuffer()
                ? Mathf.Max(0, clusteredLightingData.areaLightCount)
                : 0;
            m_SupportsVolumetricBigTileLightList = supportsClusteredFiniteLights
                && m_PunctualLightCount > 0
                && HasBoundBigTileVolumetricLightListBuffer();
            m_SupportsClusteredPunctualLights = supportsClusteredFiniteLights
                && m_PunctualLightCount > 0
                && HasBoundPunctualLightResources();
            m_SupportsClusteredAreaLights = supportsClusteredFiniteLights
                && m_AreaLightCount > 0
                && HasBoundAreaLightResources();
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
            return HasBoundPunctualLightBuffer()
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer)
                && !ReferenceEquals(m_LogBaseBuffer, m_LocalLogBaseBuffer);
        }

        private bool HasBoundAreaLightResources()
        {
            return HasBoundAreaLightBuffer()
                && !ReferenceEquals(m_LayeredOffsetBuffer, m_LocalLayeredOffsetBuffer)
                && !ReferenceEquals(m_LayeredLightListBuffer, m_LocalLayeredLightListBuffer);
        }

        private bool HasBoundBigTileLightListBuffer()
        {
            return !ReferenceEquals(m_BigTileLightListBuffer, m_LocalBigTileLightListBuffer);
        }

        private bool HasBoundBigTileVolumetricLightListBuffer()
        {
            return !ReferenceEquals(m_BigTileVolumetricLightListBuffer, m_LocalBigTileVolumetricLightListBuffer)
                || m_FrameDataBigTileVolumetricLightListBuffer != null;
        }

        private RenderGraphBuffer GetBigTileVolumetricLightListBufferForBinding()
        {
            if (HasBoundBigTileVolumetricLightListBuffer())
            {
                return !ReferenceEquals(m_BigTileVolumetricLightListBuffer, m_LocalBigTileVolumetricLightListBuffer)
                    ? m_BigTileVolumetricLightListBuffer
                    : m_FrameDataBigTileVolumetricLightListBuffer;
            }

            return HasBoundBigTileLightListBuffer()
                ? m_BigTileLightListBuffer
                : m_LocalBigTileVolumetricLightListBuffer;
        }

        private bool HasBoundPunctualLightBuffer()
        {
            return !ReferenceEquals(m_PunctualLightBuffer, m_LocalPunctualLightBuffer);
        }

        private bool HasBoundAreaLightBuffer()
        {
            return !ReferenceEquals(m_AreaLightBuffer, m_LocalAreaLightBuffer);
        }

        private void SetLightLoopBuffer(ComputeCommandBuffer cmd, int kernel, int propertyId, RenderGraphBuffer buffer)
        {
            if (buffer == null || !buffer.innerHandle.IsValid())
                return;

            cmd.SetComputeBufferParam(m_Shader, kernel, propertyId, buffer.innerHandle);
        }

        private void ConfigureCameraDepthTexture(int width, int height)
        {
            if (m_CameraDepth?.desc == null)
                return;

            m_CameraDepth.desc.Width = width;
            m_CameraDepth.desc.Height = height;
            m_CameraDepth.desc.DepthBufferBits = DepthBits.Depth32;
            m_CameraDepth.desc.ColorFormat = GraphicsFormat.None;
            m_CameraDepth.desc.FilterMode = FilterMode.Point;
            m_CameraDepth.desc.WrapMode = TextureWrapMode.Clamp;
            m_CameraDepth.desc.ClearBuffer = false;
        }

        private bool CanExecute()
        {
            return m_Shader != null
                && m_ClearKernel >= 0
                && m_LightingKernel >= 0
                && m_FilterKernel >= 0
                && m_VBufferLighting?.innerHandle.IsValid() == true;
        }
    }
}
