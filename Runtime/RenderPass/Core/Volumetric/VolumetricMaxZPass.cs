using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VolumetricMaxZPass : ComputePass, IAsyncComputeSupportedPass
    {
        private const int ThreadGroupSizeX = 8;
        private const int ThreadGroupSizeY = 8;
        private const string BuildKernelName = "BuildVBufferMaxZ";

        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepth");
        private static readonly int VBufferMaxZId = Shader.PropertyToID("_VBufferMaxZ");

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepth;

        [RenderGraphResource(Name = "VBufferMaxZ", Access = AccessFlags.Write)]
        private RenderGraphTexture m_VBufferMaxZ;

        private ComputeShader m_Shader;
        private int m_BuildKernel = -1;
        private int m_DispatchX = 1;
        private int m_DispatchY = 1;
        private int m_CameraWidth = 1;
        private int m_CameraHeight = 1;
        private VividVolumetricFogSettings m_Settings;
        private ShaderVariablesVolumetric m_ShaderVariables;

        public VolumetricMaxZPass()
        {
            profilingSampler = new ProfilingSampler(nameof(VolumetricMaxZPass));
            m_CameraDepth = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_CameraDepth.desc.FilterMode = FilterMode.Point;
            m_VBufferMaxZ = CreateVBufferMaxZTexture("VBufferMaxZ");
        }

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.VolumetricMaxZCompute;
            if (m_Shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find volumetric MaxZ compute shader for {nameof(VolumetricMaxZPass)}.");
                return;
            }

            m_BuildKernel = m_Shader.FindKernel(BuildKernelName);
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
            ConfigureVBufferMaxZTexture(m_VBufferMaxZ, m_Settings.VBufferParameters, "VBufferMaxZ");
            m_DispatchX = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportWidth, ThreadGroupSizeX);
            m_DispatchY = CoreUtils.DivRoundUp(m_Settings.VBufferParameters.ViewportHeight, ThreadGroupSizeY);

            volumetricData.settings = m_Settings;
            volumetricData.shaderVariables = m_ShaderVariables;
            volumetricData.VBufferMaxZ = m_VBufferMaxZ;
        }

        public override void Record(ComputePassContext context)
        {
            if (!CanExecute())
                return;

            var cmd = context.cmd;
            using (new ProfilingScope(cmd, profilingSampler))
            {
                ConstantBuffer.Push(cmd, m_ShaderVariables, m_Shader, ShaderVariablesVolumetricId);
                cmd.SetComputeTextureParam(m_Shader, m_BuildKernel, CameraDepthId, m_CameraDepth.innerHandle);
                cmd.SetComputeTextureParam(m_Shader, m_BuildKernel, VBufferMaxZId, m_VBufferMaxZ.innerHandle);
                cmd.DispatchCompute(m_Shader, m_BuildKernel, m_DispatchX, m_DispatchY, 1);
            }
        }

        public override void Dispose()
        {
            m_Shader = null;
            m_BuildKernel = -1;
            m_Settings = default;
            m_ShaderVariables = default;
        }

        internal static RenderGraphTexture CreateVBufferMaxZTexture(string name)
        {
            return new RenderGraphTexture
            {
                desc = new RenderGraphTextureDesc
                {
                    Width = 1,
                    Height = 1,
                    Slices = 1,
                    Dimension = TextureDimension.Tex2D,
                    ColorFormat = GraphicsFormat.R32_SFloat,
                    DepthBufferBits = DepthBits.None,
                    MsaaSamples = MSAASamples.None,
                    FilterMode = FilterMode.Point,
                    WrapMode = TextureWrapMode.Clamp,
                    ClearBuffer = true,
                    ClearColor = Color.clear,
                    EnableRandomWrite = true,
                    Name = name
                }
            };
        }

        internal static void ConfigureVBufferMaxZTexture(
            RenderGraphTexture texture,
            in VBufferParameters parameters,
            string name)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = parameters.ViewportWidth;
            texture.desc.Height = parameters.ViewportHeight;
            texture.desc.Slices = 1;
            texture.desc.Dimension = TextureDimension.Tex2D;
            texture.desc.ColorFormat = GraphicsFormat.R32_SFloat;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
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
                && m_BuildKernel >= 0
                && m_VBufferMaxZ?.innerHandle.IsValid() == true
                && m_CameraDepth?.innerHandle.IsValid() == true;
        }
    }
}
