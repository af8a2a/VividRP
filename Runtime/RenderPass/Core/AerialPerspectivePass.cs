using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class AerialPerspectivePass : RasterPass
    {
        internal const string AerialPerspectiveShaderName = "Hidden/VividRP/AerialPerspective";

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int TransmittanceLutId = Shader.PropertyToID("_TransmittanceLUT");
        private static readonly int MultiScatteringLutId = Shader.PropertyToID("_MultiScatteringLUT");
        private static readonly int SkyCameraPositionPsId = Shader.PropertyToID("_SkyCameraPositionPS");
        private static readonly int SkySunDirectionId = Shader.PropertyToID("_SkySunDirection");
        private static readonly int SkySunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int SkyPlanetParamsId = Shader.PropertyToID("_SkyPlanetParams");
        private static readonly int SkyFogParamsId = Shader.PropertyToID("_SkyFogParams");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ColorInput;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "TransmittanceLUT", Access = AccessFlags.Read)]
        private RenderGraphTexture m_TransmittanceLUT;

        [RenderGraphResource(Name = "MultiScatteringLUT", Access = AccessFlags.Read)]
        private RenderGraphTexture m_MultiScatteringLUT;

        [RenderGraphResource(
            Name = "OutputColor",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private bool m_IsActive;
        private PhysicallyBasedSkyShaderParameters m_Parameters;

        public AerialPerspectivePass()
        {
            profilingSampler = new ProfilingSampler(nameof(AerialPerspectivePass));

            m_ColorInput = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_TransmittanceLUT = RenderGraphTexture.CreateInput("TransmittanceLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_MultiScatteringLUT = RenderGraphTexture.CreateInput("MultiScatteringLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputColor", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.AerialPerspectiveShader;
            shader ??= Shader.Find(AerialPerspectiveShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{AerialPerspectiveShaderName}' for {nameof(AerialPerspectivePass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)
                && m_Parameters.skyFogParams.x > 0.5f;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_ColorInput?.desc);
            var height = ResolveOutputDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_ColorInput?.desc);
            ConfigureOutputTexture(width, height, m_ColorInput?.desc);
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_Material == null
                || m_ColorInput?.innerHandle.IsValid() != true
                || m_OutputTexture?.innerHandle.IsValid() != true)
            {
                return;
            }

            var inputColor = ResolveTexture(m_ColorInput.innerHandle);
            if (inputColor == null)
                return;

            var depthTexture = ResolveTexture(m_DepthTexture.innerHandle) ?? Texture2D.whiteTexture;
            var transmittanceLut = ResolveTexture(m_TransmittanceLUT.innerHandle) ?? Texture2D.blackTexture;
            var multiScatteringLut = ResolveTexture(m_MultiScatteringLUT.innerHandle) ?? Texture2D.blackTexture;
            var fogParams = m_IsActive
                            && depthTexture != Texture2D.whiteTexture
                            && transmittanceLut != Texture2D.blackTexture
                            && multiScatteringLut != Texture2D.blackTexture
                ? m_Parameters.skyFogParams
                : Vector4.zero;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(InputColorId, inputColor);
            mpb.SetTexture(DepthTextureId, depthTexture);
            mpb.SetTexture(TransmittanceLutId, transmittanceLut);
            mpb.SetTexture(MultiScatteringLutId, multiScatteringLut);
            mpb.SetVector(SkyCameraPositionPsId, m_Parameters.skyCameraPositionPS);
            mpb.SetVector(SkySunDirectionId, m_Parameters.skySunDirection);
            mpb.SetVector(SkySunColorId, m_Parameters.skySunColor);
            mpb.SetVector(SkyPlanetParamsId, m_Parameters.skyPlanetParams);
            mpb.SetVector(SkyFogParamsId, fogParams);

            CoreUtils.DrawFullScreen(context.cmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = sourceDescriptor != null && sourceDescriptor.ColorFormat != GraphicsFormat.None
                ? sourceDescriptor.ColorFormat
                : GraphicsFormat.R16G16B16A16_SFloat;
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = false;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Dimension = sourceDescriptor?.Dimension ?? TextureDimension.Tex2D;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor?.Slices ?? 1);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor?.UseDynamicScale ?? false;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor?.UseDynamicScaleExplicit ?? false;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor?.ScaleFactor ?? Vector2.one;
        }

        private static int ResolveOutputDimension(
            System.Func<RenderGraphTextureDesc, int> selector,
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            params RenderGraphTextureDesc[] descriptors)
        {
            for (var i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor == null
                    || descriptor.Width <= 1
                    || descriptor.Height <= 1)
                {
                    continue;
                }

                return Mathf.Max(1, selector(descriptor));
            }

            if (actualCameraDimension > 0)
                return actualCameraDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static Texture ResolveTexture(RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }
    }
}
