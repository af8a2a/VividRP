using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VolumetricDensityPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        public const string FogVolumeVoxelizeShaderTagName = "FogVolumeVoxelize";
        public const string VolumetricFogVFXShaderTagName = "VolumetricFogVFX";
        public const string VolumetricFogVFXOverdrawDebugShaderTagName = "VolumetricFogVFXOverdrawDebug";

        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const int ThreadGroupSizeZ = 4;
        private const int ClearMaterialThreadGroupSizeX = 64;
        private const int ComputeMaterialThreadGroupSizeX = 32;
        private const string ClearKernelName = "ClearVBufferDensity";
        private const string VoxelizeKernelName = "VoxelizeVBufferDensity";
        private const string ClearMaterialKernelName = "ClearVolumetricMaterialRenderingParameters";
        private const string ComputeMaterialKernelName = "ComputeVolumetricMaterialRenderingParameters";

        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");
        private static readonly int VBufferDensityId = Shader.PropertyToID("_VBufferDensity");
        private static readonly int VolumeBoundsId = Shader.PropertyToID("_VolumeBounds");
        private static readonly int VolumetricVisibleGlobalIndicesBufferId = Shader.PropertyToID("_VolumetricVisibleGlobalIndicesBuffer");
        private static readonly int VolumetricGlobalIndirectArgsBufferId = Shader.PropertyToID("_VolumetricGlobalIndirectArgsBuffer");
        private static readonly int VolumetricGlobalIndirectionBufferId = Shader.PropertyToID("_VolumetricGlobalIndirectionBuffer");
        private static readonly int VolumetricMaterialDataId = Shader.PropertyToID("_VolumetricMaterialData");
        private static readonly int VolumeCountId = Shader.PropertyToID("_VolumeCount");
        private static readonly int MaxVolumeCountId = Shader.PropertyToID("_MaxVolumeCount");
        private static readonly int MaxSliceCountId = Shader.PropertyToID("_MaxSliceCount");
        private static readonly int ViewCountId = Shader.PropertyToID("_ViewCount");

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepth;

        [RenderGraphResource(Name = "VBufferDensity", Access = AccessFlags.Write)]
        private RenderGraphTexture m_VBufferDensity;

        [RenderGraphResource(Name = "FogVolumeVFXRenderList", Access = AccessFlags.Read)]
        private readonly RenderGraphRenderList m_FogVolumeVFXRenderList;

        [RenderGraphResource(Name = "VolumeBounds", Access = AccessFlags.Read)]
        private readonly RenderGraphBuffer m_VolumeBounds;

        [RenderGraphResource(Name = "VolumetricVisibleGlobalIndices", Access = AccessFlags.Read)]
        private readonly RenderGraphBuffer m_VisibleGlobalIndices;

        [RenderGraphResource(Name = "VolumetricGlobalIndirectArgs", Access = AccessFlags.ReadWrite)]
        private readonly RenderGraphBuffer m_GlobalIndirectArgs;

        [RenderGraphResource(Name = "VolumetricGlobalIndirection", Access = AccessFlags.ReadWrite)]
        private readonly RenderGraphBuffer m_GlobalIndirection;

        [RenderGraphResource(Name = "VolumetricMaterialData", Access = AccessFlags.ReadWrite)]
        private readonly RenderGraphBuffer m_VolumetricMaterialData;

        private ComputeShader m_DensityShader;
        private ComputeShader m_VolumetricMaterialShader;
        private int m_ClearKernel = -1;
        private int m_VoxelizeKernel = -1;
        private int m_ClearMaterialKernel = -1;
        private int m_ComputeMaterialKernel = -1;
        private int m_DispatchX = 1;
        private int m_DispatchY = 1;
        private int m_DispatchZ = 1;
        private int m_CameraWidth = 1;
        private int m_CameraHeight = 1;
        private int m_ViewCount = 1;
        private VividVolumetricFogSettings m_Settings;
        private ShaderVariablesVolumetric m_ShaderVariables;
        private int m_LocalFogCount;
        private int m_MaterialFogCount;

        public VolumetricDensityPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VolumetricDensityPass));
            m_CameraDepth = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_CameraDepth.desc.FilterMode = FilterMode.Point;
            m_VBufferDensity = CreateVBufferTexture("VBufferDensity");
            m_FogVolumeVFXRenderList = new RenderGraphRenderList
            {
                desc = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = new[]
                    {
                        VolumetricFogVFXShaderTagName,
                        FogVolumeVoxelizeShaderTagName
                    },
                    RenderQueueRange = RenderGraphRenderQueueRange.All,
                    SortingCriteria = SortingCriteria.RendererPriority,
                    RendererConfiguration = PerObjectData.None,
                    ExcludeObjectMotionVectors = false
                }
            };
            m_VolumeBounds = VividLocalVolumetricFogManager.volumeBoundsBuffer;
            m_VisibleGlobalIndices = VividLocalVolumetricFogManager.visibleGlobalIndicesBuffer;
            m_GlobalIndirectArgs = VividLocalVolumetricFogManager.globalIndirectArgsBuffer;
            m_GlobalIndirection = VividLocalVolumetricFogManager.globalIndirectionBuffer;
            m_VolumetricMaterialData = VividLocalVolumetricFogManager.volumetricMaterialDataBuffer;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            m_DensityShader = resources?.VolumetricDensityCompute;
            if (m_DensityShader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find volumetric density compute shader for {nameof(VolumetricDensityPass)}.");
                return;
            }

            m_ClearKernel = m_DensityShader.FindKernel(ClearKernelName);
            m_VoxelizeKernel = m_DensityShader.FindKernel(VoxelizeKernelName);

            m_VolumetricMaterialShader = resources?.VolumetricMaterialCompute;
            if (m_VolumetricMaterialShader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find volumetric material compute shader for {nameof(VolumetricDensityPass)}.");
                return;
            }

            m_ClearMaterialKernel = m_VolumetricMaterialShader.FindKernel(ClearMaterialKernelName);
            m_ComputeMaterialKernel = m_VolumetricMaterialShader.FindKernel(ComputeMaterialKernelName);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var camera = cameraData.camera;
            m_CameraWidth = CameraDimensionUtility.ResolveCameraDimension(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width);
            m_CameraHeight = CameraDimensionUtility.ResolveCameraDimension(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height);
            m_ViewCount = 1;

            m_Settings = VividVolumetricUtility.ResolveSettings(frameData);
            m_LocalFogCount = m_Settings.Enabled
                ? VividLocalVolumetricFogManager.PrepareVisibleFogs(camera, m_Settings.MaxLocalVolumetricFogCount)
                : 0;
            m_MaterialFogCount = m_Settings.Enabled
                ? VividLocalVolumetricFogManager.materialFogCount
                : 0;
            m_ShaderVariables = VividVolumetricUtility.BuildShaderVariables(
                m_Settings,
                m_CameraWidth,
                m_CameraHeight,
                m_LocalFogCount,
                cameraData);

            ConfigureCameraDepthTexture(m_CameraWidth, m_CameraHeight);
            ConfigureVBufferTexture(m_VBufferDensity, m_Settings.VBufferParameters, "VBufferDensity", clear: true);
            m_DispatchX = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportWidth, ThreadGroupSizeX);
            m_DispatchY = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportHeight, ThreadGroupSizeY);
            m_DispatchZ = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.SliceCount, ThreadGroupSizeZ);

            var volumetricData = frameData.GetOrCreate<VividVolumetricData>();
            volumetricData.settings = m_Settings;
            volumetricData.shaderVariables = m_ShaderVariables;
            volumetricData.VBufferDensity = m_VBufferDensity;
            volumetricData.localVolumetricFogBuffer = null;
            volumetricData.localVolumetricFogCount = m_LocalFogCount;
            volumetricData.enabled = m_Settings.Enabled;
            volumetricData.gaussianFilteringEnabled = m_Settings.GaussianFilteringEnabled;
        }

        public override void Record(UnsafePassContext context)
        {
            if (!CanExecuteDensity())
                return;

            var cmd = context.GetNativeCommandBuffer();
            ConstantBuffer.PushGlobal(cmd, m_ShaderVariables, ShaderVariablesVolumetricId);
            cmd.SetComputeTextureParam(m_DensityShader, m_ClearKernel, VBufferDensityId, m_VBufferDensity.innerHandle);
            cmd.DispatchCompute(m_DensityShader, m_ClearKernel, m_DispatchX, m_DispatchY, m_DispatchZ);

            if (!m_Settings.Enabled)
                return;

            cmd.SetComputeTextureParam(m_DensityShader, m_VoxelizeKernel, VBufferDensityId, m_VBufferDensity.innerHandle);
            cmd.DispatchCompute(m_DensityShader, m_VoxelizeKernel, m_DispatchX, m_DispatchY, 1);
            RecordFogVolumeAndVFXVoxelization(cmd);
        }

        public override void Dispose()
        {
            m_DensityShader = null;
            m_VolumetricMaterialShader = null;
            m_ClearKernel = -1;
            m_VoxelizeKernel = -1;
            m_ClearMaterialKernel = -1;
            m_ComputeMaterialKernel = -1;
            m_ViewCount = 1;
            m_LocalFogCount = 0;
            m_MaterialFogCount = 0;
        }

        internal static RenderGraphTexture CreateVBufferTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Slices = VividVolumetricFogVolume.DefaultVolumeSliceCount,
                    Dimension = TextureDimension.Tex3D,
                    ColorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    DepthBufferBits = DepthBits.None,
                    MsaaSamples = MSAASamples.None,
                    FilterMode = FilterMode.Bilinear,
                    WrapMode = TextureWrapMode.Clamp,
                    ClearBuffer = true,
                    ClearColor = Color.clear,
                    EnableRandomWrite = true,
                    Name = name
                }
            };
        }

        internal static void ConfigureVBufferTexture(
            RenderGraphTexture texture,
            in VBufferParameters parameters,
            string name,
            bool clear)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = parameters.ViewportWidth;
            texture.desc.Height = parameters.ViewportHeight;
            texture.desc.Slices = parameters.SliceCount;
            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.ColorFormat = GraphicsFormat.R16G16B16A16_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = clear;
            texture.desc.ClearColor = clear ? Color.clear : Color.black;
            texture.desc.EnableRandomWrite = true;
            texture.desc.BindTextureMS = false;
            texture.desc.Name = name;
        }

        private void RecordFogVolumeAndVFXVoxelization(CommandBuffer cmd)
        {
            if (!CanExecuteFogVolumeAndVFXVoxelization())
                return;

            var indirectArgs = VividLocalVolumetricFogManager.globalIndirectArgsGraphicsBuffer;
            var indirection = VividLocalVolumetricFogManager.globalIndirectionGraphicsBuffer;
            var materialData = VividLocalVolumetricFogManager.volumetricMaterialDataGraphicsBuffer;
            var volumeBounds = VividLocalVolumetricFogManager.volumeBoundsGraphicsBuffer;
            var visibleGlobalIndices = VividLocalVolumetricFogManager.visibleGlobalIndicesGraphicsBuffer;

            cmd.SetComputeBufferParam(m_VolumetricMaterialShader, m_ClearMaterialKernel, VolumetricGlobalIndirectArgsBufferId, indirectArgs);
            cmd.SetComputeIntParam(m_VolumetricMaterialShader, MaxVolumeCountId, indirectArgs.count);
            cmd.DispatchCompute(
                m_VolumetricMaterialShader,
                m_ClearMaterialKernel,
                Mathf.Max(1, CoreUtils.DivRoundUp(indirectArgs.count, ClearMaterialThreadGroupSizeX)),
                1,
                1);

            cmd.SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumeBoundsId, volumeBounds);
            cmd.SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumetricVisibleGlobalIndicesBufferId, visibleGlobalIndices);
            cmd.SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumetricGlobalIndirectArgsBufferId, indirectArgs);
            cmd.SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumetricGlobalIndirectionBufferId, indirection);
            cmd.SetComputeBufferParam(m_VolumetricMaterialShader, m_ComputeMaterialKernel, VolumetricMaterialDataId, materialData);
            cmd.SetComputeIntParam(m_VolumetricMaterialShader, VolumeCountId, m_MaterialFogCount);
            cmd.SetComputeIntParam(m_VolumetricMaterialShader, MaxSliceCountId, m_Settings.VBufferParameters.SliceCount);
            cmd.SetComputeIntParam(m_VolumetricMaterialShader, ViewCountId, m_ViewCount);
            ConstantBuffer.PushGlobal(cmd, m_ShaderVariables, ShaderVariablesVolumetricId);
            cmd.DispatchCompute(
                m_VolumetricMaterialShader,
                m_ComputeMaterialKernel,
                Mathf.Max(1, CoreUtils.DivRoundUp(m_MaterialFogCount * m_ViewCount, ComputeMaterialThreadGroupSizeX)),
                1,
                1);

            InsertVolumetricMaterialComputeToDrawFence(cmd);

            cmd.SetGlobalBuffer(VolumetricGlobalIndirectionBufferId, indirection);
            cmd.SetGlobalBuffer(VolumetricMaterialDataId, materialData);
            // Bind all VBuffer slices so SV_RenderTargetArrayIndex writes every voxel slice.
            CoreUtils.SetRenderTarget(cmd, m_VBufferDensity);
            cmd.SetViewport(new Rect(
                0.0f,
                0.0f,
                m_Settings.VBufferParameters.ViewportWidth,
                m_Settings.VBufferParameters.ViewportHeight));
            VividLocalVolumetricFogManager.RecordVolumetricMaterialDrawCalls(cmd);

            if (m_FogVolumeVFXRenderList?.IsValid == true)
                cmd.DrawRendererList(m_FogVolumeVFXRenderList);
        }

        private static void InsertVolumetricMaterialComputeToDrawFence(CommandBuffer cmd)
        {
            var fence = cmd.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.ComputeProcessing);
            cmd.WaitOnAsyncGraphicsFence(fence, SynchronisationStageFlags.AllGPUOperations);
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

        private bool CanExecuteDensity()
        {
            return m_DensityShader != null
                && m_ClearKernel >= 0
                && m_VoxelizeKernel >= 0
                && m_VBufferDensity?.innerHandle.IsValid() == true;
        }

        private bool CanExecuteFogVolumeAndVFXVoxelization()
        {
            return m_VolumetricMaterialShader != null
                && m_ClearMaterialKernel >= 0
                && m_ComputeMaterialKernel >= 0
                && SystemInfo.supportsRenderTargetArrayIndexFromVertexShader
                && VividLocalVolumetricFogManager.globalIndirectArgsGraphicsBuffer != null
                && VividLocalVolumetricFogManager.globalIndirectArgsGraphicsBuffer.IsValid()
                && VividLocalVolumetricFogManager.globalIndirectionGraphicsBuffer != null
                && VividLocalVolumetricFogManager.globalIndirectionGraphicsBuffer.IsValid()
                && VividLocalVolumetricFogManager.volumetricMaterialDataGraphicsBuffer != null
                && VividLocalVolumetricFogManager.volumetricMaterialDataGraphicsBuffer.IsValid()
                && VividLocalVolumetricFogManager.volumeBoundsGraphicsBuffer != null
                && VividLocalVolumetricFogManager.volumeBoundsGraphicsBuffer.IsValid()
                && VividLocalVolumetricFogManager.visibleGlobalIndicesGraphicsBuffer != null
                && VividLocalVolumetricFogManager.visibleGlobalIndicesGraphicsBuffer.IsValid();
        }
    }
}
