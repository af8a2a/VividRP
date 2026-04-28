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

        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepth");
        private static readonly int DirectionalShadowTextureId = Shader.PropertyToID("_DirectionalShadowTexture");
        private static readonly int VBufferMaxZId = Shader.PropertyToID("_VBufferMaxZ");
        private static readonly int VBufferMaxZEnabledId = Shader.PropertyToID("_VBufferMaxZEnabled");
        private static readonly int VBufferDensityId = Shader.PropertyToID("_VBufferDensity");
        private static readonly int VBufferLightingId = Shader.PropertyToID("_VBufferLighting");
        private static readonly int VBufferLightingInputId = Shader.PropertyToID("_VBufferLightingInput");
        private static readonly int VBufferLightingOutputId = Shader.PropertyToID("_VBufferLightingOutput");
        private static readonly int DirectionalLightsId = Shader.PropertyToID("_DirectionalLights");
        private static readonly int DirectionalLightCountId = Shader.PropertyToID("_DirectionalLightCount");
        private static readonly int MainDirectionalLightIndexId = Shader.PropertyToID("_MainDirectionalLightIndex");
        private static readonly int PunctualLightsId = Shader.PropertyToID("_PunctualLights");
        private static readonly int AreaLightsId = Shader.PropertyToID("_AreaLights");
        private static readonly int ClusteredPunctualLightGridEnabledId = Shader.PropertyToID("_ClusteredPunctualLightGridEnabled");
        private static readonly int ClusteredAreaLightGridEnabledId = Shader.PropertyToID("_ClusteredAreaLightGridEnabled");
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
        private static readonly int ClusterTileSizeId = Shader.PropertyToID("_ClusterTileSize");
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

        [RenderGraphResource(Name = "VBufferMaxZ", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferMaxZ;

        [RenderGraphResource(Name = "VBufferDensity", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferDensity;

        [RenderGraphResource(Name = "VBufferLighting", Access = AccessFlags.Write)]
        private RenderGraphTexture m_VBufferLighting;

        [RenderGraphResource(Name = "VBufferLightingFiltered", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_VBufferLightingFiltered;

        [RenderGraphResource(Name = "DirectionalLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_DirectionalLightBuffer;

        [RenderGraphResource(Name = "PunctualLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_PunctualLightBuffer;

        [RenderGraphResource(Name = "AreaLights", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_AreaLightBuffer;

        [RenderGraphResource(Name = "LayeredOffset", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredOffsetBuffer;

        [RenderGraphResource(Name = "LayeredLightList", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LayeredLightListBuffer;

        [RenderGraphResource(Name = "LogBaseBuffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_LogBaseBuffer;

        private readonly RenderGraphTexture m_LocalDirectionalShadowTexture;
        private readonly RenderGraphTexture m_LocalVBufferMaxZ;
        private readonly RenderGraphBuffer m_LocalDirectionalLightBuffer;
        private readonly RenderGraphBuffer m_LocalPunctualLightBuffer;
        private readonly RenderGraphBuffer m_LocalAreaLightBuffer;
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
        private bool m_IsLogBaseBufferEnabled;

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
            m_LocalVBufferMaxZ = VolumetricMaxZPass.CreateVBufferMaxZTexture("VBufferMaxZ");
            m_VBufferMaxZ = m_LocalVBufferMaxZ;
            m_VBufferDensity = VolumetricDensityPass.CreateVBufferTexture("VBufferDensity");
            m_VBufferLighting = VolumetricDensityPass.CreateVBufferTexture("VBufferLighting");
            m_VBufferLighting.desc.ClearColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            m_VBufferLightingFiltered = VolumetricDensityPass.CreateVBufferTexture("VBufferLightingFiltered");
            m_LocalDirectionalLightBuffer = RenderGraphBuffer.CreateStructured("DirectionalLights", 1, VividLightData.DirectionalLightData.Stride);
            m_LocalPunctualLightBuffer = RenderGraphBuffer.CreateStructured("PunctualLights", 1, VividLightData.PunctualLightData.Stride);
            m_LocalAreaLightBuffer = RenderGraphBuffer.CreateStructured("AreaLights", 1, VividLightData.AreaLightData.Stride);
            m_LocalLayeredOffsetBuffer = RenderGraphBuffer.CreateStructured("LayeredOffset", 1, sizeof(uint));
            m_LocalLayeredLightListBuffer = RenderGraphBuffer.CreateStructured("LayeredLightList", 1, sizeof(uint));
            m_LocalLogBaseBuffer = RenderGraphBuffer.CreateStructured("LogBaseBuffer", 1, sizeof(float));
            m_DirectionalLightBuffer = m_LocalDirectionalLightBuffer;
            m_PunctualLightBuffer = m_LocalPunctualLightBuffer;
            m_AreaLightBuffer = m_LocalAreaLightBuffer;
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
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferLighting, m_Settings.VBufferParameters, "VBufferLighting", clear: true);
            VolumetricDensityPass.ConfigureVBufferTexture(m_VBufferLightingFiltered, m_Settings.VBufferParameters, "VBufferLightingFiltered", clear: false);
            m_DispatchX = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportWidth, ThreadGroupSizeX);
            m_DispatchY = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportHeight, ThreadGroupSizeY);
            m_DispatchZ = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.SliceCount, ThreadGroupSizeZ);
            m_FilterDispatchZ = Mathf.Max(m_Settings.VBufferParameters.SliceCount, 1);
            PrepareClusteredLightingParameters(frameData);

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

                if (!m_Settings.Enabled || m_VBufferDensity?.innerHandle.IsValid() != true)
                    return;

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
            m_FilterDispatchZ = 1;
        }

        private void BindSharedTextures(ComputePassContext context, ComputeCommandBuffer cmd, int kernel, RenderGraphTexture lightingTarget)
        {
            cmd.SetComputeTextureParam(m_Shader, kernel, CameraDepthId, m_CameraDepth.innerHandle);
            BindVBufferMaxZ(context, cmd, kernel);
            cmd.SetComputeTextureParam(m_Shader, kernel, VBufferDensityId, m_VBufferDensity.innerHandle);
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
            cmd.SetComputeIntParam(m_Shader, ClusteredPunctualLightGridEnabledId, m_SupportsClusteredPunctualLights ? 1 : 0);
            cmd.SetComputeIntParam(m_Shader, ClusteredAreaLightGridEnabledId, m_SupportsClusteredAreaLights ? 1 : 0);
            cmd.SetComputeIntParam(m_Shader, ClusteredDecalGridEnabledId, 0);
            cmd.SetComputeIntParam(m_Shader, ClusterTileSizeId, m_ClusterTileSize);
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

            SetLightLoopBuffer(cmd, kernel, DirectionalLightsId, m_DirectionalLightBuffer);
            SetLightLoopBuffer(cmd, kernel, PunctualLightsId, m_PunctualLightBuffer);
            SetLightLoopBuffer(cmd, kernel, AreaLightsId, m_AreaLightBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredOffsetId, m_LayeredOffsetBuffer);
            SetLightLoopBuffer(cmd, kernel, LayeredLightListId, m_LayeredLightListBuffer);
            SetLightLoopBuffer(cmd, kernel, LogBaseBufferId, m_LogBaseBuffer);
        }

        private void PrepareClusteredLightingParameters(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            var camera = cameraData.camera;

            m_DirectionalLightCount = 0;
            m_MainDirectionalLightIndex = -1;
            m_ClusterTileSize = LightGridPass.ClusterTileSize;
            m_ClusterSliceCount = LightGridPass.ClusterSliceCount;
            m_ClusterTileCountX = Mathf.Max(1, Mathf.CeilToInt(m_CameraWidth / (float)m_ClusterTileSize));
            m_ClusterTileCountY = Mathf.Max(1, Mathf.CeilToInt(m_CameraHeight / (float)m_ClusterTileSize));
            m_ClusterNearClip = Mathf.Max(camera != null ? camera.nearClipPlane : 0.1f, 0.01f);
            m_ClusterFarClip = Mathf.Max(camera != null ? camera.farClipPlane : 1000.0f, m_ClusterNearClip + 0.01f);
            m_ClusterIsOrthographic = camera != null && camera.orthographic ? 1 : 0;
            m_ClusterScale = 0.0f;
            m_ClusterBase = LightGridPass.ClusterLogBase;
            m_ClusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;
            m_SupportsClusteredPunctualLights = false;
            m_SupportsClusteredAreaLights = false;
            m_IsLogBaseBufferEnabled = false;

            if (clusteredLightingData == null)
                return;

            m_MainDirectionalLightIndex = clusteredLightingData.mainDirectionalLightIndex;
            m_ClusterTileSize = clusteredLightingData.clusterTileSize > 0 ? clusteredLightingData.clusterTileSize : m_ClusterTileSize;
            m_ClusterSliceCount = clusteredLightingData.clusterSliceCount > 0 ? clusteredLightingData.clusterSliceCount : m_ClusterSliceCount;
            m_ClusterTileCountX = clusteredLightingData.clusterTileCountX > 0 ? clusteredLightingData.clusterTileCountX : m_ClusterTileCountX;
            m_ClusterTileCountY = clusteredLightingData.clusterTileCountY > 0 ? clusteredLightingData.clusterTileCountY : m_ClusterTileCountY;
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
            m_SupportsClusteredPunctualLights = supportsClusteredFiniteLights
                && clusteredLightingData.punctualLightCount > 0
                && HasBoundPunctualLightResources();
            m_SupportsClusteredAreaLights = supportsClusteredFiniteLights
                && clusteredLightingData.areaLightCount > 0
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
