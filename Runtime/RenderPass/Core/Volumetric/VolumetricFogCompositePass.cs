using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class VolumetricFogCompositePass : UnsafePass
    {
        internal const string ShaderName = "Hidden/VividRP/VolumetricFogComposite";

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int CameraDepthId = Shader.PropertyToID("_CameraDepth");
        private static readonly int VBufferLightingId = Shader.PropertyToID("_VBufferLighting");
        private static readonly int VolumetricEnabledId = Shader.PropertyToID("_VolumetricEnabled");
        private static readonly int ShaderVariablesVolumetricId = Shader.PropertyToID("ShaderVariablesVolumetric");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Color;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_CameraDepth;

        [RenderGraphResource(Name = "VBufferLighting", Access = AccessFlags.Read)]
        private RenderGraphTexture m_VBufferLighting;

        [RenderGraphResource(
            Name = "OutputColor",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputColor;

        private Material m_Material;
        private Texture3D m_FallbackVBufferLighting;
        private bool m_Enabled;
        private ShaderVariablesVolumetric m_ShaderVariables;

        public VolumetricFogCompositePass()
        {
            profilingSampler = new ProfilingSampler(nameof(VolumetricFogCompositePass));
            m_Color = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_CameraDepth = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_CameraDepth.desc.FilterMode = FilterMode.Point;
            m_VBufferLighting = VolumetricDensityPass.CreateVBufferTexture("VBufferLighting");
            m_OutputColor = RenderGraphTexture.CreateOutput("OutputColor", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputColor.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            var shader = PipelineResourceManager.Get<VividRPCoreResources>()?.VolumetricFogCompositeShader;
            shader ??= Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ShaderName}' for {nameof(VolumetricFogCompositePass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            EnsureFallbackVBufferLighting();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = CameraDimensionUtility.ResolveCameraDimension(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width);
            var height = CameraDimensionUtility.ResolveCameraDimension(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height);

            var volumetricData = frameData.GetOrCreate<VividVolumetricData>();
            m_Enabled = volumetricData.enabled;
            m_ShaderVariables = volumetricData.shaderVariables;
            ConfigureInputTextures(width, height);
            ConfigureOutputTexture(width, height, m_Color.desc);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_Material == null
                || m_Color?.innerHandle.IsValid() != true
                || m_OutputColor?.innerHandle.IsValid() != true)
            {
                return;
            }

            var inputColor = TextureResolveUtility.ResolveTexture(m_Color.innerHandle);
            if (inputColor == null)
                return;

            var depthTexture = TextureResolveUtility.ResolveTexture(m_CameraDepth.innerHandle) ?? Texture2D.whiteTexture;
            var vBufferLighting = TextureResolveUtility.ResolveTexture(m_VBufferLighting.innerHandle);
            var hasVBufferLighting = HasValidVBuffer(vBufferLighting);
            if (!hasVBufferLighting)
                EnsureFallbackVBufferLighting();

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(InputColorId, inputColor);
            SetDepthTexture(mpb, depthTexture);
            mpb.SetTexture(VBufferLightingId, hasVBufferLighting ? vBufferLighting : m_FallbackVBufferLighting);
            mpb.SetFloat(VolumetricEnabledId, m_Enabled && hasVBufferLighting ? 1.0f : 0.0f);

            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            ConstantBuffer.PushGlobal(cmd, m_ShaderVariables, ShaderVariablesVolumetricId);
            cmd.SetRenderTarget(m_OutputColor);
            CoreUtils.DrawFullScreen(cmd, m_Material, mpb);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            if (m_FallbackVBufferLighting != null)
            {
                CoreUtils.Destroy(m_FallbackVBufferLighting);
                m_FallbackVBufferLighting = null;
            }
        }

        private void ConfigureInputTextures(int width, int height)
        {
            if (m_Color?.desc != null)
            {
                m_Color.desc.Width = width;
                m_Color.desc.Height = height;
                m_Color.desc.ClearBuffer = false;
            }

            if (m_CameraDepth?.desc != null)
            {
                m_CameraDepth.desc.Width = width;
                m_CameraDepth.desc.Height = height;
                m_CameraDepth.desc.DepthBufferBits = DepthBits.Depth32;
                m_CameraDepth.desc.ColorFormat = GraphicsFormat.None;
                m_CameraDepth.desc.ClearBuffer = false;
            }
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputColor?.desc == null)
                return;

            m_OutputColor.desc.Width = width;
            m_OutputColor.desc.Height = height;
            m_OutputColor.desc.ColorFormat = sourceDescriptor != null && sourceDescriptor.ColorFormat != GraphicsFormat.None
                ? sourceDescriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
            m_OutputColor.desc.DepthBufferBits = DepthBits.None;
            m_OutputColor.desc.MsaaSamples = MSAASamples.None;
            m_OutputColor.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputColor.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputColor.desc.ClearBuffer = false;
            m_OutputColor.desc.UseMipMap = false;
            m_OutputColor.desc.AutoGenerateMips = false;
            m_OutputColor.desc.MipCount = 1;
            m_OutputColor.desc.EnableRandomWrite = false;
            m_OutputColor.desc.Dimension = TextureDimension.Tex2D;
            m_OutputColor.desc.Slices = 1;
        }

        private void EnsureFallbackVBufferLighting()
        {
            if (m_FallbackVBufferLighting != null)
                return;

            m_FallbackVBufferLighting = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false)
            {
                name = "VividFallbackVBufferLighting",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_FallbackVBufferLighting.SetPixels(new[] { new Color(0.0f, 0.0f, 0.0f, 1.0f) });
            m_FallbackVBufferLighting.Apply(false, true);
        }

        private static void SetDepthTexture(MaterialPropertyBlock properties, Texture texture)
        {
            if (properties == null)
                return;

            if (texture is RenderTexture renderTexture
                && (renderTexture.depth > 0 || renderTexture.depthStencilFormat != GraphicsFormat.None))
            {
                properties.SetTexture(CameraDepthId, renderTexture, RenderTextureSubElement.Depth);
                return;
            }

            properties.SetTexture(CameraDepthId, texture);
        }

        private static bool HasValidVBuffer(Texture texture)
        {
            if (texture == null || texture.dimension != TextureDimension.Tex3D)
                return false;

            if (texture is RenderTexture renderTexture)
                return renderTexture.IsCreated() && renderTexture.volumeDepth > 1;

            return texture is Texture3D texture3D && texture3D.depth > 1;
        }
    }
}
