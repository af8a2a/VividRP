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
        internal const string DualSlabSidecarKeyword = "VIVID_DUAL_SLAB_SIDECAR_OUTPUT";

        private static readonly int VisibilityBufferId = Shader.PropertyToID("_VisibilityBuffer");
        private static readonly int VisibilityBufferScaleBiasId = Shader.PropertyToID("_VisibilityBufferScaleBias");
        private static readonly int VisibilityBufferAttributes0Id = Shader.PropertyToID("_VisibilityBufferAttributes0");
        private static readonly int VisibilityBufferAttributes0ScaleBiasId = Shader.PropertyToID("_VisibilityBufferAttributes0ScaleBias");
        private static readonly int VisibilityBufferAttributes1Id = Shader.PropertyToID("_VisibilityBufferAttributes1");
        private static readonly int VisibilityBufferAttributes1ScaleBiasId = Shader.PropertyToID("_VisibilityBufferAttributes1ScaleBias");
        private static readonly int VisibilityBufferBarycentricsId = Shader.PropertyToID("_VisibilityBufferBarycentrics");
        private static readonly int VisibilityBufferBarycentricsScaleBiasId = Shader.PropertyToID("_VisibilityBufferBarycentricsScaleBias");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "VisibilityBufferAttributes0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes0;

        [RenderGraphResource(Name = "VisibilityBufferAttributes1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes1;

        [RenderGraphResource(Name = "VisibilityBufferBarycentrics", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Barycentrics;
        
        [RenderGraphResource(
            Name = "GBuffer0",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 0)]
        private RenderGraphTexture m_GBuffer0;

        [RenderGraphResource(
            Name = "GBuffer1",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 1)]
        private RenderGraphTexture m_GBuffer1;

        [RenderGraphResource(
            Name = "GBuffer2",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 2)]
        private RenderGraphTexture m_GBuffer2;

        [RenderGraphResource(
            Name = "GBuffer3",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 3)]
        private RenderGraphTexture m_GBuffer3;

        [RenderGraphResource(
            Name = "DiffuseIrradiance",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 4)]
        // Keep the legacy field name as the serialized RenderGraph port key.
        private RenderGraphTexture m_GBuffer4;

        [RenderGraphResource(
            Name = "LayerAux0",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 5)]
        private RenderGraphTexture m_LayerAux0;

        [RenderGraphResource(
            Name = "LayerAux1",
            Access = AccessFlags.ReadWrite,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable,
            AttachmentIndex = 6)]
        private RenderGraphTexture m_LayerAux1;

        private readonly RenderGraphTexture m_DefaultGBuffer0;
        private readonly RenderGraphTexture m_DefaultGBuffer1;
        private readonly RenderGraphTexture m_DefaultGBuffer2;
        private readonly RenderGraphTexture m_DefaultGBuffer3;
        private readonly RenderGraphTexture m_DefaultDiffuseIrradiance;
        private readonly RenderGraphTexture m_DefaultLayerAux0;
        private readonly RenderGraphTexture m_DefaultLayerAux1;
        private readonly RenderTargetIdentifier[] m_GBufferColorTargets = new RenderTargetIdentifier[5];
        private readonly RenderTargetIdentifier[] m_DualSlabSidecarTargets = new RenderTargetIdentifier[2];
        private readonly MaterialPropertyBlock m_DrawProperties = new MaterialPropertyBlock();
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        [SerializeField, Min(1f)]
        private float m_VirtualTextureFeedbackSampleRate = 4.0f;

        private Material m_Material;
        private Material m_DualSlabSidecarMaterial;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private int m_FrameIndex;

        public VisibilityBufferGBufferResolvePass()
        {
            profilingSampler = new ProfilingSampler(nameof(VisibilityBufferGBufferResolvePass));

            m_VisibilityBuffer = RenderGraphTexture.CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_VisibilityBuffer.desc.FilterMode = FilterMode.Point;
            m_Attributes0 = RenderGraphTexture.CreateInput(
                "VisibilityBufferAttributes0",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_Attributes0.desc.FilterMode = FilterMode.Point;
            m_Attributes1 = RenderGraphTexture.CreateInput(
                "VisibilityBufferAttributes1",
                GraphicsFormat.R16G16B16A16_SFloat);
            m_Attributes1.desc.FilterMode = FilterMode.Point;
            m_Barycentrics = RenderGraphTexture.CreateInput(
                "VisibilityBufferBarycentrics",
                GraphicsFormat.R16G16_SFloat);
            m_Barycentrics.desc.FilterMode = FilterMode.Point;

            m_GBuffer0 = RenderGraphTexture.CreateColorTarget("GBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_GBuffer1 = RenderGraphTexture.CreateColorTarget("GBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_GBuffer2 = RenderGraphTexture.CreateColorTarget("GBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_GBuffer3 = RenderGraphTexture.CreateColorTarget("GBuffer3", GraphicsFormat.B10G11R11_UFloatPack32);
            m_GBuffer3.desc.EnableRandomWrite = true;
            m_GBuffer4 = RenderGraphTexture.CreateColorTarget(
                "DiffuseIrradiance",
                GraphicsFormat.B10G11R11_UFloatPack32);
            m_LayerAux0 = RenderGraphTexture.CreateColorTarget(
                "LayerAux0",
                GraphicsFormat.R8G8B8A8_SRGB);
            m_LayerAux1 = RenderGraphTexture.CreateColorTarget(
                "LayerAux1",
                GraphicsFormat.R8G8B8A8_UNorm);

            m_DefaultGBuffer0 = m_GBuffer0;
            m_DefaultGBuffer1 = m_GBuffer1;
            m_DefaultGBuffer2 = m_GBuffer2;
            m_DefaultGBuffer3 = m_GBuffer3;
            m_DefaultDiffuseIrradiance = m_GBuffer4;
            m_DefaultLayerAux0 = m_LayerAux0;
            m_DefaultLayerAux1 = m_LayerAux1;
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
            m_DualSlabSidecarMaterial = CoreUtils.CreateEngineMaterial(shader);
            CoreUtils.SetKeyword(
                m_DualSlabSidecarMaterial,
                DualSlabSidecarKeyword,
                true);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
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

            ConfigurePassOwnedTarget(m_GBuffer0, m_DefaultGBuffer0, width, height, GraphicsFormat.R8G8B8A8_SRGB, false, "GBuffer0");
            ConfigurePassOwnedTarget(m_GBuffer1, m_DefaultGBuffer1, width, height, GraphicsFormat.A2B10G10R10_UNormPack32, false, "GBuffer1");
            ConfigurePassOwnedTarget(m_GBuffer2, m_DefaultGBuffer2, width, height, GraphicsFormat.R8G8B8A8_UNorm, false, "GBuffer2");
            ConfigurePassOwnedTarget(m_GBuffer3, m_DefaultGBuffer3, width, height, GraphicsFormat.B10G11R11_UFloatPack32, true, "GBuffer3");
            ConfigurePassOwnedTarget(
                m_GBuffer4,
                m_DefaultDiffuseIrradiance,
                width,
                height,
                GraphicsFormat.B10G11R11_UFloatPack32,
                false,
                "DiffuseIrradiance");
            ConfigurePassOwnedTarget(
                m_LayerAux0,
                m_DefaultLayerAux0,
                width,
                height,
                GraphicsFormat.R8G8B8A8_SRGB,
                false,
                "LayerAux0");
            ConfigurePassOwnedTarget(
                m_LayerAux1,
                m_DefaultLayerAux1,
                width,
                height,
                GraphicsFormat.R8G8B8A8_UNorm,
                false,
                "LayerAux1");
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Material == null
                || m_DualSlabSidecarMaterial == null
                || !m_VisibilityBuffer.innerHandle.IsValid()
                || !m_Attributes0.innerHandle.IsValid()
                || !m_Attributes1.innerHandle.IsValid()
                || !m_Barycentrics.innerHandle.IsValid()
                || !m_GBuffer0.innerHandle.IsValid()
                || !m_GBuffer1.innerHandle.IsValid()
                || !m_GBuffer2.innerHandle.IsValid()
                || !m_GBuffer3.innerHandle.IsValid()
                || !m_GBuffer4.innerHandle.IsValid()
                || !m_LayerAux0.innerHandle.IsValid()
                || !m_LayerAux1.innerHandle.IsValid())
            {
                return;
            }

            var visibilityTexture = m_VisibilityBuffer.innerHandle.ResolveTexture();
            var attributes0Texture = m_Attributes0.innerHandle.ResolveTexture();
            var attributes1Texture = m_Attributes1.innerHandle.ResolveTexture();
            var barycentricsTexture = m_Barycentrics.innerHandle.ResolveTexture();
            if (visibilityTexture == null
                || attributes0Texture == null
                || attributes1Texture == null
                || barycentricsTexture == null)
                return;

            VividGPUDrivenSystem system = VividGPUDrivenSystem.HasInstance
                ? VividGPUDrivenSystem.instance
                : null;
            system?.ConfigureTextureBackendKeyword(m_Material);
            system?.ConfigureTextureBackendKeyword(m_DualSlabSidecarMaterial);

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
            m_DrawProperties.SetVector(VisibilityBufferScaleBiasId, m_VisibilityBuffer.innerHandle.GetScaleBias());
            m_DrawProperties.SetTexture(VisibilityBufferAttributes0Id, attributes0Texture);
            m_DrawProperties.SetVector(
                VisibilityBufferAttributes0ScaleBiasId,
                m_Attributes0.innerHandle.GetScaleBias());
            m_DrawProperties.SetTexture(VisibilityBufferAttributes1Id, attributes1Texture);
            m_DrawProperties.SetVector(
                VisibilityBufferAttributes1ScaleBiasId,
                m_Attributes1.innerHandle.GetScaleBias());
            m_DrawProperties.SetTexture(VisibilityBufferBarycentricsId, barycentricsTexture);
            m_DrawProperties.SetVector(
                VisibilityBufferBarycentricsScaleBiasId,
                m_Barycentrics.innerHandle.GetScaleBias());

            BindGBufferTargets(nativeCmd);
            CoreUtils.DrawFullScreen(nativeCmd, m_Material, m_DrawProperties, 0);
            if (hasFeedback)
                nativeCmd.ClearRandomWriteTargets();

            // VT feedback occupies u5-u7, so keep the core draw at five MRTs
            // and emit the optional Dual Slab sidecar after releasing UAVs.
            BindDualSlabSidecarTargets(nativeCmd);
            CoreUtils.DrawFullScreen(
                nativeCmd,
                m_DualSlabSidecarMaterial,
                m_DrawProperties,
                0);
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
            if (m_DualSlabSidecarMaterial != null)
            {
                CoreUtils.Destroy(m_DualSlabSidecarMaterial);
                m_DualSlabSidecarMaterial = null;
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
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
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

        private void BindDualSlabSidecarTargets(CommandBuffer cmd)
        {
            m_DualSlabSidecarTargets[0] = m_LayerAux0;
            m_DualSlabSidecarTargets[1] = m_LayerAux1;
            cmd.SetRenderTarget(
                m_DualSlabSidecarTargets,
                BuiltinRenderTextureType.None);
        }

        private static int ResolveOutputWidth(
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            RenderGraphTextureDesc descriptor)
        {
            if (descriptor.HasExplicitSize())
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
            if (descriptor.HasExplicitSize())
                return Mathf.Max(1, descriptor.Height);

            return CameraDimensionUtility.ResolveCameraDimension(
                actualCameraDimension,
                cameraDimension,
                screenDimension);
        }

    }
}
