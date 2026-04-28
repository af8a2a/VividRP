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
        internal const int MaxZTileSize = 8;
        internal const int FinalMaskDownsample = 2;
        private const string ComputeMaxZKernelName = "ComputeMaxZ";
        private const string ComputeFinalMaskKernelName = "ComputeFinalMask";
        private const string DilateMaskKernelName = "DilateMask";

        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepth");
        private static readonly int InputTextureId = Shader.PropertyToID("_InputTexture");
        private static readonly int OutputTextureId = Shader.PropertyToID("_OutputTexture");
        private static readonly int SrcOffsetAndLimitId = Shader.PropertyToID("_SrcOffsetAndLimit");
        private static readonly int DilationWidthId = Shader.PropertyToID("_DilationWidth");

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepth;

        [RenderGraphResource(Name = "VBufferMaxZ", Access = AccessFlags.Write)]
        private RenderGraphTexture m_VBufferMaxZ;

        [RenderGraphResource(Name = "VBufferMaxZ8x", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_VBufferMaxZ8x;

        [RenderGraphResource(Name = "VBufferMaxZFinalMask", Access = AccessFlags.ReadWrite)]
        [TransientResource]
        private RenderGraphTexture m_VBufferMaxZFinalMask;

        private ComputeShader m_Shader;
        private int m_ComputeMaxZKernel = -1;
        private int m_ComputeFinalMaskKernel = -1;
        private int m_DilateMaskKernel = -1;
        private int m_MaxZDispatchX = 1;
        private int m_MaxZDispatchY = 1;
        private int m_FinalMaskDispatchX = 1;
        private int m_FinalMaskDispatchY = 1;
        private int m_MaxZMaskWidth = 1;
        private int m_MaxZMaskHeight = 1;
        private int m_FinalMaskWidth = 1;
        private int m_FinalMaskHeight = 1;
        private int m_DilationWidth = 1;
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
            m_VBufferMaxZ8x = CreateVBufferMaxZTexture("VBufferMaxZ8x");
            m_VBufferMaxZFinalMask = CreateVBufferMaxZTexture("VBufferMaxZFinalMask");
        }

        public override void Create()
        {
            m_Shader = PipelineResourceManager.Get<VividRPCoreResources>()?.VolumetricMaxZCompute;
            if (m_Shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find volumetric MaxZ compute shader for {nameof(VolumetricMaxZPass)}.");
                return;
            }

            m_ComputeMaxZKernel = m_Shader.FindKernel(ComputeMaxZKernelName);
            m_ComputeFinalMaskKernel = m_Shader.FindKernel(ComputeFinalMaskKernelName);
            m_DilateMaskKernel = m_Shader.FindKernel(DilateMaskKernelName);
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
            m_MaxZMaskWidth = Mathf.Max(1, CoreUtils.DivRoundUp(m_CameraWidth, MaxZTileSize));
            m_MaxZMaskHeight = Mathf.Max(1, CoreUtils.DivRoundUp(m_CameraHeight, MaxZTileSize));
            m_FinalMaskWidth = Mathf.Max(1, CoreUtils.DivRoundUp(m_MaxZMaskWidth, FinalMaskDownsample));
            m_FinalMaskHeight = Mathf.Max(1, CoreUtils.DivRoundUp(m_MaxZMaskHeight, FinalMaskDownsample));
            m_DilationWidth = VividVolumetricUtility.ComputeMaxZDilationRadius(m_Settings.VBufferParameters.ScreenPercentage);

            ConfigureVBufferMaxZTexture(m_VBufferMaxZ8x, m_MaxZMaskWidth, m_MaxZMaskHeight, "VBufferMaxZ8x");
            ConfigureVBufferMaxZTexture(m_VBufferMaxZFinalMask, m_FinalMaskWidth, m_FinalMaskHeight, "VBufferMaxZFinalMask");
            ConfigureVBufferMaxZTexture(m_VBufferMaxZ, m_FinalMaskWidth, m_FinalMaskHeight, "VBufferMaxZ");
            m_MaxZDispatchX = m_MaxZMaskWidth;
            m_MaxZDispatchY = m_MaxZMaskHeight;
            m_FinalMaskDispatchX = CoreUtils.DivRoundUp(m_FinalMaskWidth, ThreadGroupSizeX);
            m_FinalMaskDispatchY = CoreUtils.DivRoundUp(m_FinalMaskHeight, ThreadGroupSizeY);

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

                cmd.SetComputeTextureParam(m_Shader, m_ComputeMaxZKernel, CameraDepthId, m_CameraDepth.innerHandle);
                cmd.SetComputeTextureParam(m_Shader, m_ComputeMaxZKernel, OutputTextureId, m_VBufferMaxZ8x.innerHandle);
                cmd.DispatchCompute(m_Shader, m_ComputeMaxZKernel, m_MaxZDispatchX, m_MaxZDispatchY, 1);

                cmd.SetComputeVectorParam(m_Shader, SrcOffsetAndLimitId, new Vector4(m_MaxZMaskWidth, m_MaxZMaskHeight, 0.0f, 0.0f));
                cmd.SetComputeTextureParam(m_Shader, m_ComputeFinalMaskKernel, InputTextureId, m_VBufferMaxZ8x.innerHandle);
                cmd.SetComputeTextureParam(m_Shader, m_ComputeFinalMaskKernel, OutputTextureId, m_VBufferMaxZFinalMask.innerHandle);
                cmd.DispatchCompute(m_Shader, m_ComputeFinalMaskKernel, m_FinalMaskDispatchX, m_FinalMaskDispatchY, 1);

                cmd.SetComputeFloatParam(m_Shader, DilationWidthId, m_DilationWidth);
                cmd.SetComputeVectorParam(m_Shader, SrcOffsetAndLimitId, new Vector4(m_FinalMaskWidth, m_FinalMaskHeight, 0.0f, 0.0f));
                cmd.SetComputeTextureParam(m_Shader, m_DilateMaskKernel, InputTextureId, m_VBufferMaxZFinalMask.innerHandle);
                cmd.SetComputeTextureParam(m_Shader, m_DilateMaskKernel, OutputTextureId, m_VBufferMaxZ.innerHandle);
                cmd.DispatchCompute(m_Shader, m_DilateMaskKernel, m_FinalMaskDispatchX, m_FinalMaskDispatchY, 1);
            }
        }

        public override void Dispose()
        {
            m_Shader = null;
            m_ComputeMaxZKernel = -1;
            m_ComputeFinalMaskKernel = -1;
            m_DilateMaskKernel = -1;
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
            ConfigureVBufferMaxZTexture(texture, parameters.ViewportWidth, parameters.ViewportHeight, name);
        }

        internal static void ConfigureVBufferMaxZTexture(
            RenderGraphTexture texture,
            int width,
            int height,
            string name)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Width = Mathf.Max(1, width);
            texture.desc.Height = Mathf.Max(1, height);
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
                && m_ComputeMaxZKernel >= 0
                && m_ComputeFinalMaskKernel >= 0
                && m_DilateMaskKernel >= 0
                && m_VBufferMaxZ?.innerHandle.IsValid() == true
                && m_VBufferMaxZ8x?.innerHandle.IsValid() == true
                && m_VBufferMaxZFinalMask?.innerHandle.IsValid() == true
                && m_CameraDepth?.innerHandle.IsValid() == true;
        }
    }
}
