using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VisibilityBufferGBufferResolvePass : UnsafePass, IAllowGlobalStateModificationPass
    {
        internal const string VisibilityBufferGBufferResolveShaderName = "Hidden/VividRP/GPUDriven/VisibilityBufferGBufferResolve";

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int DepthTextureScaleBiasId = Shader.PropertyToID("_DepthTextureScaleBias");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 0)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 1)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 2)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 3)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(
            Name = "GBuffer4",
            Access = AccessFlags.ReadWrite,
            AttachmentIndex = 4)]
        private RenderGraphTexture m_GBuffer4;

        private readonly RenderGraphTexture m_DefaultGBuffer0;
        private readonly RenderGraphTexture m_DefaultGBuffer1;
        private readonly RenderGraphTexture m_DefaultGBuffer2;
        private readonly RenderGraphTexture m_DefaultGBuffer3;
        private readonly RenderGraphTexture m_DefaultGBuffer4;
        private readonly RenderTargetIdentifier[] m_GBufferColorTargets = new RenderTargetIdentifier[5];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        [SerializeField, Min(1f)]
        private float m_VirtualTextureFeedbackSampleRate = 4.0f;

        private Material m_Material;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private int m_FrameIndex;

        public VisibilityBufferGBufferResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferGBufferResolvePass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;

            m_DepthTexture = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);
            m_DepthTexture.desc.FilterMode = FilterMode.Point;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBuffer4 = RenderGraphTexture.CreateColorTarget("GBuffer4", GraphicsFormat.R16G16B16A16_SFloat);

            m_DefaultGBuffer0 = m_GBuffer0;
            m_DefaultGBuffer1 = m_GBuffer1;
            m_DefaultGBuffer2 = m_GBuffer2;
            m_DefaultGBuffer3 = m_GBuffer3;
            m_DefaultGBuffer4 = m_GBuffer4;
        }

        public override void Create()
        {
            var shader = Shader.Find(VisibilityBufferGBufferResolveShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find shader '{VisibilityBufferGBufferResolveShaderName}' for {nameof(VisibilityBufferGBufferResolvePass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            m_FrameIndex = cameraData.frameIndex >= 0 ? cameraData.frameIndex : Time.frameCount;
            var visibilityBufferDescriptor = m_VisibilityBuffer?.desc;
            var width = ResolveOutputWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                visibilityBufferDescriptor);
            var height = ResolveOutputHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                visibilityBufferDescriptor);

            ConfigurePassOwnedTarget(m_GBuffer0, m_DefaultGBuffer0, width, height, GraphicsFormat.R8G8B8A8_UNorm, false, "GBuffer0");
            ConfigurePassOwnedTarget(m_GBuffer1, m_DefaultGBuffer1, width, height, GraphicsFormat.A2B10G10R10_UNormPack32, false, "GBuffer1");
            ConfigurePassOwnedTarget(m_GBuffer2, m_DefaultGBuffer2, width, height, GraphicsFormat.R8G8B8A8_UNorm, false, "GBuffer2");
            ConfigurePassOwnedTarget(m_GBuffer3, m_DefaultGBuffer3, width, height, GraphicsFormat.B10G11R11_UFloatPack32, true, "GBuffer3");
            ConfigurePassOwnedTarget(m_GBuffer4, m_DefaultGBuffer4, width, height, GraphicsFormat.R16G16B16A16_SFloat, false, "GBuffer4");
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Material == null
                || !m_VisibilityBuffer.innerHandle.IsValid()
                || !m_GBuffer0.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_GBuffer2.innerHandle.IsValid()
                || !m_GBuffer3.innerHandle.IsValid()
                || !m_GBuffer4.innerHandle.IsValid())
            {
                return;
            }

            var visibilityTexture = TextureResolveUtility.ResolveTexture(m_VisibilityBuffer.innerHandle);
            if (visibilityTexture == null)
                return;

            var depthTexture = TextureResolveUtility.ResolveTexture(m_DepthTexture.innerHandle) ?? Texture2D.whiteTexture;

            VividGPUDrivenSystem system = VividGPUDrivenSystem.HasInstance
                ? VividGPUDrivenSystem.instance
                : null;
            system?.ConfigureTextureBackendKeyword(m_Material);

            bool hasFeedback = false;
            var nativeCmd = context.GetNativeCommandBuffer();
            if (system?.UsesVirtualTexture == true)
            {
                if (!GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                        nativeCmd,
                        m_VirtualTextureFrameData,
                        m_VirtualTextureSpaceParams,
                        m_VirtualTextureMipOffsets,
                        m_VirtualTextureLayerFallbacks,
                        m_FrameIndex,
                        Mathf.RoundToInt(m_VirtualTextureFeedbackSampleRate),
                        out VirtualTextureSpaceBinding binding))
                {
                    return;
                }

                hasFeedback = VirtualTextureFeedbackBindingUtility.BindFeedbackTargets(nativeCmd, binding);
            }

            m_DrawProperties.Clear();
            m_DrawProperties.SetTexture(VisibilityBufferId, visibilityTexture);
            m_DrawProperties.SetTexture(DepthTextureId, depthTexture);
            m_DrawProperties.SetVector(VisibilityBufferScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_VisibilityBuffer.innerHandle));
            m_DrawProperties.SetVector(DepthTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_DepthTexture.innerHandle));

            BindGBufferTargets(nativeCmd);
            CoreUtils.DrawFullScreen(nativeCmd, m_Material, m_DrawProperties, 0);
            if (hasFeedback)
                nativeCmd.ClearRandomWriteTargets();
        }

        public override void Dispose()
        {
            m_VirtualTextureFrameData = null;
            m_FrameIndex = 0;
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private static void ConfigurePassOwnedTarget(
            RenderGraphTexture texture,
            RenderGraphTexture defaultTexture,
            int width,
            int height,
            GraphicsFormat format,
            bool enableRandomWrite,
            string name)
        {
            if (!ReferenceEquals(texture, defaultTexture) || texture?.desc == null)
                return;

            texture.desc.Width = width;
            texture.desc.Height = height;
            texture.desc.ColorFormat = format;
            texture.desc.DepthBufferBits = DepthBits.None;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.ClearBuffer = false;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
            texture.desc.EnableRandomWrite = enableRandomWrite;
            texture.desc.BindTextureMS = false;
            texture.desc.Name = name;
        }

        private void BindGBufferTargets(CommandBuffer cmd)
        {
            m_GBufferColorTargets[0] = m_GBuffer0;
            m_GBufferColorTargets[1] = m_GBuffer1;
            m_GBufferColorTargets[2] = m_GBuffer2;
            m_GBufferColorTargets[3] = m_GBuffer3;
            m_GBufferColorTargets[4] = m_GBuffer4;
            cmd.SetRenderTarget(m_GBufferColorTargets, BuiltinRenderTextureType.None);
        }

        private static int ResolveOutputWidth(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (RenderGraphTextureDescUtility.HasExplicitSize(descriptor))
                return Mathf.Max(1, descriptor.Width);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

        private static int ResolveOutputHeight(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (RenderGraphTextureDescUtility.HasExplicitSize(descriptor))
                return Mathf.Max(1, descriptor.Height);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

    }
}
