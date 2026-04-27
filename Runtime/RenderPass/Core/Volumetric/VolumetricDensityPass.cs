using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VolumetricDensityPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const int ThreadGroupSizeZ = 4;
        private const string ClearKernelName = "ClearVBufferDensity";
        private const string VoxelizeKernelName = "VoxelizeVBufferDensity";

        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepth");
        private static readonly int VBufferDensityId = Shader.PropertyToID("_VBufferDensity");
        private static readonly int LocalVolumetricFogsId = Shader.PropertyToID("_LocalVolumetricFogs");

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepth;

        [RenderGraphResource(Name = "VBufferDensity", Access = AccessFlags.Write)]
        private RenderGraphTexture m_VBufferDensity;

        [RenderGraphResource(Name = "LocalVolumetricFogs", Access = AccessFlags.Read)]
        private readonly RenderGraphBuffer m_LocalVolumetricFogBuffer;

        private ComputeShader m_Shader;
        private int m_ClearKernel = -1;
        private int m_VoxelizeKernel = -1;
        private int m_DispatchX = 1;
        private int m_DispatchY = 1;
        private int m_DispatchZ = 1;
        private int m_CameraWidth = 1;
        private int m_CameraHeight = 1;
        private VividVolumetricFogSettings m_Settings;
        private ShaderVariablesVolumetric m_ShaderVariables;
        private int m_LocalFogCount;

        public VolumetricDensityPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VolumetricDensityPass));
            m_CameraDepth = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_CameraDepth.desc.FilterMode = FilterMode.Point;
            m_VBufferDensity = CreateVBufferTexture("VBufferDensity");
            m_LocalVolumetricFogBuffer = VividLocalVolumetricFogManager.buffer;
        }

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.VolumetricDensityCompute;
            if (m_Shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find volumetric density compute shader for {nameof(VolumetricDensityPass)}.");
                return;
            }

            m_ClearKernel = m_Shader.FindKernel(ClearKernelName);
            m_VoxelizeKernel = m_Shader.FindKernel(VoxelizeKernelName);
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

            m_Settings = VividVolumetricUtility.ResolveSettings(frameData);
            m_LocalFogCount = m_Settings.Enabled
                ? VividLocalVolumetricFogManager.PrepareVisibleFogs(camera)
                : 0;
            m_ShaderVariables = VividVolumetricUtility.BuildShaderVariables(
                m_Settings,
                m_CameraWidth,
                m_CameraHeight,
                m_LocalFogCount);

            ConfigureCameraDepthTexture(m_CameraWidth, m_CameraHeight);
            ConfigureVBufferTexture(m_VBufferDensity, m_Settings.VBufferParameters, "VBufferDensity", clear: true);
            m_DispatchX = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportWidth, ThreadGroupSizeX);
            m_DispatchY = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportHeight, ThreadGroupSizeY);
            m_DispatchZ = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.SliceCount, ThreadGroupSizeZ);

            var volumetricData = frameData.GetOrCreate<VividVolumetricData>();
            volumetricData.settings = m_Settings;
            volumetricData.shaderVariables = m_ShaderVariables;
            volumetricData.VBufferDensity = m_VBufferDensity;
            volumetricData.localVolumetricFogBuffer = m_LocalVolumetricFogBuffer;
            volumetricData.localVolumetricFogCount = m_LocalFogCount;
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
                cmd.SetComputeTextureParam(m_Shader, m_ClearKernel, VBufferDensityId, m_VBufferDensity.innerHandle);
                cmd.DispatchCompute(m_Shader, m_ClearKernel, m_DispatchX, m_DispatchY, m_DispatchZ);

                if (!m_Settings.Enabled)
                    return;

                cmd.SetComputeTextureParam(m_Shader, m_VoxelizeKernel, CameraDepthId, m_CameraDepth.innerHandle);
                cmd.SetComputeTextureParam(m_Shader, m_VoxelizeKernel, VBufferDensityId, m_VBufferDensity.innerHandle);
                cmd.SetComputeBufferParam(m_Shader, m_VoxelizeKernel, LocalVolumetricFogsId, m_LocalVolumetricFogBuffer.innerHandle);
                cmd.DispatchCompute(m_Shader, m_VoxelizeKernel, m_DispatchX, m_DispatchY, m_DispatchZ);
            }
        }

        public override void Dispose()
        {
            m_Shader = null;
            m_ClearKernel = -1;
            m_VoxelizeKernel = -1;
            m_LocalFogCount = 0;
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
                && m_VoxelizeKernel >= 0
                && m_VBufferDensity?.innerHandle.IsValid() == true
                && m_LocalVolumetricFogBuffer?.innerHandle.IsValid() == true;
        }
    }
}
