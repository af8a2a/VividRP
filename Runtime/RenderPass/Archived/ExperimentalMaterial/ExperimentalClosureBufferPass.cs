using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.GPUDriven;
using VividRP.Runtime.GPUDriven.VirtualTexture;
using UnityMaterial = UnityEngine.Material;

namespace VividRP.Runtime.RenderPass.Experimental.Material
{
    public sealed class ExperimentalClosureBufferPass : UnsafePass, IAllowGlobalStateModificationPass
    {
        internal const string ResolveShaderName = "Hidden/VividRP/Experimental/ClosureBufferResolve";

        private static readonly int s_VisibilityBufferId = Shader.PropertyToID("_ExperimentalVisibilityBuffer");
        private static readonly int s_Attributes0Id = Shader.PropertyToID("_ExperimentalVisibilityAttributes0");
        private static readonly int s_Attributes1Id = Shader.PropertyToID("_ExperimentalVisibilityAttributes1");
        private static readonly int s_DepthId = Shader.PropertyToID("_ExperimentalDepthTexture");
        private static readonly int s_VisibilityScaleBiasId = Shader.PropertyToID("_ExperimentalVisibilityScaleBias");
        private static readonly int s_Attributes0ScaleBiasId = Shader.PropertyToID("_ExperimentalAttributes0ScaleBias");
        private static readonly int s_Attributes1ScaleBiasId = Shader.PropertyToID("_ExperimentalAttributes1ScaleBias");
        private static readonly int s_DepthScaleBiasId = Shader.PropertyToID("_ExperimentalDepthScaleBias");

        [RenderGraphResource(Name = "VisibilityBuffer", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VisibilityBuffer;

        [RenderGraphResource(Name = "VisibilityBufferAttributes0", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes0;

        [RenderGraphResource(Name = "VisibilityBufferAttributes1", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Attributes1;

        [RenderGraphResource(Name = "Depth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Depth;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer0", Access = AccessFlags.Write, AttachmentIndex = 0, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer0;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer1", Access = AccessFlags.Write, AttachmentIndex = 1, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer1;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer2", Access = AccessFlags.Write, AttachmentIndex = 2, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer2;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer3", Access = AccessFlags.Write, AttachmentIndex = 3, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer3;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer4", Access = AccessFlags.Write, AttachmentIndex = 4, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer4;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer5", Access = AccessFlags.Write, AttachmentIndex = 5, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer5;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer6", Access = AccessFlags.Write, AttachmentIndex = 6, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer6;

        [RenderGraphResource(Name = "ExperimentalClosureBuffer7", Access = AccessFlags.Write, AttachmentIndex = 7, BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_ClosureBuffer7;

        private readonly RenderTargetIdentifier[] m_ColorTargets = new RenderTargetIdentifier[8];
        private readonly MaterialPropertyBlock m_DrawProperties = new();
        private readonly float[] m_VirtualTextureSpaceParams = new float[VirtualTextureSpaceShaderParams.IntCount];
        private readonly float[] m_VirtualTextureMipOffsets = new float[VirtualTextureFeedbackProcessor.MaxMipCount];
        private readonly Vector4[] m_VirtualTextureLayerFallbacks = new Vector4[VTStackDesc.MaxLayerCount];

        [SerializeField, Min(1.0f)]
        private float m_VirtualTextureFeedbackSampleRate = 4.0f;

        private UnityMaterial m_Material;
        private VividVirtualTextureFrameData m_VirtualTextureFrameData;
        private int m_FrameIndex;

        public ExperimentalClosureBufferPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ExperimentalClosureBufferPass));
            m_VisibilityBuffer = CreateInput("VisibilityBuffer", GraphicsFormat.R32G32_UInt);
            m_Attributes0 = CreateInput("VisibilityBufferAttributes0", GraphicsFormat.R32G32B32A32_SFloat);
            m_Attributes1 = CreateInput("VisibilityBufferAttributes1", GraphicsFormat.R16G16B16A16_SFloat);
            m_Depth = RenderGraphTexture.CreateInput("Depth", GraphicsFormat.None, DepthBits.Depth32);

            m_ClosureBuffer0 = CreateOutput("ExperimentalClosureBuffer0", GraphicsFormat.R8G8B8A8_SRGB);
            m_ClosureBuffer1 = CreateOutput("ExperimentalClosureBuffer1", GraphicsFormat.A2B10G10R10_UNormPack32);
            m_ClosureBuffer2 = CreateOutput("ExperimentalClosureBuffer2", GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer3 = CreateOutput("ExperimentalClosureBuffer3", GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer4 = CreateOutput("ExperimentalClosureBuffer4", GraphicsFormat.B10G11R11_UFloatPack32);
            m_ClosureBuffer5 = CreateOutput("ExperimentalClosureBuffer5", GraphicsFormat.R16G16B16A16_SFloat);
            m_ClosureBuffer6 = CreateOutput("ExperimentalClosureBuffer6", GraphicsFormat.R8G8B8A8_UNorm);
            m_ClosureBuffer7 = CreateOutput("ExperimentalClosureBuffer7", GraphicsFormat.R8G8B8A8_UNorm);
        }

        public override void Create()
        {
            Shader shader = PipelineResourceManager.Get<VividRPCoreResources>()?.ExperimentalClosureBufferResolveShader
                            ?? Shader.Find(ResolveShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[VividRP] Could not find shader '{ResolveShaderName}' for {nameof(ExperimentalClosureBufferPass)}.");
                return;
            }
            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Resize(int width, int height)
        {
            ResizeOutput(m_ClosureBuffer0, width, height);
            ResizeOutput(m_ClosureBuffer1, width, height);
            ResizeOutput(m_ClosureBuffer2, width, height);
            ResizeOutput(m_ClosureBuffer3, width, height);
            ResizeOutput(m_ClosureBuffer4, width, height);
            ResizeOutput(m_ClosureBuffer5, width, height);
            ResizeOutput(m_ClosureBuffer6, width, height);
            ResizeOutput(m_ClosureBuffer7, width, height);
        }

        public override void Prepare(ContextContainer frameData)
        {
            VividCameraData cameraData = frameData.GetOrCreate<VividCameraData>();
            m_VirtualTextureFrameData = frameData.GetOrCreate<VividVirtualTextureFrameData>();
            VirtualTextureSystem.RegisterPageTableReadDependencies(this, m_VirtualTextureFrameData);
            m_FrameIndex = cameraData.frameIndex >= 0 ? cameraData.frameIndex : Time.frameCount;
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Material == null
                || m_VisibilityBuffer?.IsValid() != true
                || m_Attributes0?.IsValid() != true
                || m_Attributes1?.IsValid() != true
                || m_Depth?.IsValid() != true
                || !OutputsAreValid())
            {
                return;
            }

            Texture visibility = m_VisibilityBuffer.innerHandle.ResolveTexture();
            Texture attributes0 = m_Attributes0.innerHandle.ResolveTexture();
            Texture attributes1 = m_Attributes1.innerHandle.ResolveTexture();
            Texture depth = m_Depth.innerHandle.ResolveTexture();
            if (visibility == null || attributes0 == null || attributes1 == null || depth == null)
                return;

            CommandBuffer cmd = context.GetNativeCommandBuffer();
            VividGPUDrivenSystem system = VividGPUDrivenSystem.HasInstance ? VividGPUDrivenSystem.instance : null;
            if (system?.IsAvailable != true)
                return;

            system.ConfigureTextureBackendKeyword(m_Material);
            system.BindGlobals(cmd);

            bool hasFeedback = false;
            if (system.UsesVirtualTexture)
            {
                if (!GPUDrivenVirtualTextureBindingUtility.BindSpaceGlobals(
                        cmd,
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

                hasFeedback = VirtualTextureFeedbackBindingUtility.BindFeedbackTargets(cmd, binding);
            }

            m_DrawProperties.Clear();
            m_DrawProperties.SetTexture(s_VisibilityBufferId, visibility);
            m_DrawProperties.SetTexture(s_Attributes0Id, attributes0);
            m_DrawProperties.SetTexture(s_Attributes1Id, attributes1);
            m_DrawProperties.SetTexture(s_DepthId, depth);
            m_DrawProperties.SetVector(s_VisibilityScaleBiasId, m_VisibilityBuffer.innerHandle.GetScaleBias());
            m_DrawProperties.SetVector(s_Attributes0ScaleBiasId, m_Attributes0.innerHandle.GetScaleBias());
            m_DrawProperties.SetVector(s_Attributes1ScaleBiasId, m_Attributes1.innerHandle.GetScaleBias());
            m_DrawProperties.SetVector(s_DepthScaleBiasId, m_Depth.innerHandle.GetScaleBias());

            BindClosureTargets(cmd);
            cmd.ClearRenderTarget(clearDepth: false, clearColor: true, Color.clear);
            CoreUtils.DrawFullScreen(cmd, m_Material, m_DrawProperties, 0);
            if (hasFeedback)
                cmd.ClearRandomWriteTargets();
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

        private bool OutputsAreValid()
        {
            return m_ClosureBuffer0?.IsValid() == true
                   && m_ClosureBuffer1?.IsValid() == true
                   && m_ClosureBuffer2?.IsValid() == true
                   && m_ClosureBuffer3?.IsValid() == true
                   && m_ClosureBuffer4?.IsValid() == true
                   && m_ClosureBuffer5?.IsValid() == true
                   && m_ClosureBuffer6?.IsValid() == true
                   && m_ClosureBuffer7?.IsValid() == true;
        }

        private void BindClosureTargets(CommandBuffer cmd)
        {
            m_ColorTargets[0] = m_ClosureBuffer0;
            m_ColorTargets[1] = m_ClosureBuffer1;
            m_ColorTargets[2] = m_ClosureBuffer2;
            m_ColorTargets[3] = m_ClosureBuffer3;
            m_ColorTargets[4] = m_ClosureBuffer4;
            m_ColorTargets[5] = m_ClosureBuffer5;
            m_ColorTargets[6] = m_ClosureBuffer6;
            m_ColorTargets[7] = m_ClosureBuffer7;
            cmd.SetRenderTarget(m_ColorTargets, BuiltinRenderTextureType.None);
        }

        private static RenderGraphTexture CreateInput(string name, GraphicsFormat format)
        {
            RenderGraphTexture texture = RenderGraphTexture.CreateInput(name, format);
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.MsaaSamples = MSAASamples.None;
            return texture;
        }

        private static RenderGraphTexture CreateOutput(string name, GraphicsFormat format)
        {
            RenderGraphTexture texture = RenderGraphTexture.CreateColorTarget(name, format);
            texture.desc.FilterMode = FilterMode.Point;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.MsaaSamples = MSAASamples.None;
            texture.desc.ClearBuffer = true;
            texture.desc.ClearColor = Color.clear;
            return texture;
        }

        private static void ResizeOutput(RenderGraphTexture texture, int width, int height)
        {
            if (texture?.desc == null)
                return;
            texture.Resize(Mathf.Max(1, width), Mathf.Max(1, height));
        }
    }
}
