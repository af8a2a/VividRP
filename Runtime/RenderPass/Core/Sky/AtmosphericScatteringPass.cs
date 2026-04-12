using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public sealed class AtmosphericScatteringPass : UnsafePass
    {
        internal const string OpaqueAtmosphericScatteringPassName = "Opaque Atmospheric Scattering";
        internal const string OpaqueAtmosphericScatteringShaderName = "Hidden/VividRP/OpaqueAtmosphericScattering";

        private static readonly int InputColorId = Shader.PropertyToID("_InputColor");
        private static readonly int DepthTextureId = Shader.PropertyToID("_DepthTexture");
        private static readonly int AtmosphericScatteringLutId = Shader.PropertyToID("_AtmosphericScatteringLUT");
        private static readonly int PixelCoordToViewDirWSId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private static readonly int SkyFogParamsId = Shader.PropertyToID("_SkyFogParams");

        [RenderGraphResource(Name = "Color", Access = AccessFlags.Read)]
        private RenderGraphTexture m_ColorInput;

        [RenderGraphResource(Name = "CameraDepth", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DepthTexture;

        [RenderGraphResource(Name = "AtmosphericScatteringLUT", Access = AccessFlags.Read)]
        private RenderGraphTexture m_AtmosphericScatteringLUT;

        [RenderGraphResource(
            Name = "OutputColor",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        private Material m_Material;
        private bool m_IsActive;
        private bool m_HasMaterialParameters;
        private PhysicallyBasedSkyShaderParameters m_Parameters;
        private PhysicallyBasedSkyMaterialParameters m_MaterialParameters;
        private Texture3D m_FallbackAtmosphericScatteringLut;

        public AtmosphericScatteringPass()
        {
            profilingSampler = new ProfilingSampler(OpaqueAtmosphericScatteringPassName);

            m_ColorInput = RenderGraphTexture.CreateInput("Color", GraphicsFormat.R16G16B16A16_SFloat);
            m_DepthTexture = RenderGraphTexture.CreateInput("CameraDepth", GraphicsFormat.None, DepthBits.Depth32);
            m_AtmosphericScatteringLUT = RenderGraphTexture.CreateInput("AtmosphericScatteringLUT", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture = RenderGraphTexture.CreateOutput("OutputColor", GraphicsFormat.R16G16B16A16_SFloat);
            m_OutputTexture.desc.ClearBuffer = false;
            ConfigureAtmosphericScatteringDescriptor(m_AtmosphericScatteringLUT);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.AerialPerspectiveShader;
            shader ??= Shader.Find(OpaqueAtmosphericScatteringShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{OpaqueAtmosphericScatteringShaderName}' for {nameof(AtmosphericScatteringPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            EnsureFallbackAtmosphericScatteringLut();
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_IsActive = PhysicallyBasedSkyShaderParameterBuilder.TryBuild(frameData, out m_Parameters)
                && m_Parameters.skyFogParams.x > 0.5f;
            m_HasMaterialParameters = PhysicallyBasedSkyShaderParameterBuilder.TryBuildMaterialParameters(frameData, out m_MaterialParameters);

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

        public override void Record(UnsafeGraphContext context)
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
            var atmosphericScatteringLut = ResolveTexture(m_AtmosphericScatteringLUT.innerHandle);
            var hasValidAtmosphericScatteringLut = HasValidAtmosphericScatteringLut(atmosphericScatteringLut);
            if (!hasValidAtmosphericScatteringLut)
                EnsureFallbackAtmosphericScatteringLut();

            var fogParams = m_IsActive
                            && depthTexture != Texture2D.whiteTexture
                            && m_HasMaterialParameters
                            && hasValidAtmosphericScatteringLut
                ? m_Parameters.skyFogParams
                : Vector4.zero;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(InputColorId, inputColor);
            mpb.SetTexture(DepthTextureId, depthTexture);
            mpb.SetTexture(
                AtmosphericScatteringLutId,
                hasValidAtmosphericScatteringLut ? atmosphericScatteringLut : m_FallbackAtmosphericScatteringLut);
            mpb.SetMatrix(PixelCoordToViewDirWSId, m_IsActive ? m_Parameters.pixelCoordToViewDirWS : Matrix4x4.identity);
            mpb.SetVector(SkyFogParamsId, fogParams);
            if (m_HasMaterialParameters)
                PhysicallyBasedSkyMaterialPropertyBinder.Apply(mpb, m_MaterialParameters, VividVolumeManagerUtility.GetPhysicallyBasedSkyVolume());

            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            nativeCmd.SetRenderTarget(m_OutputTexture);
            CoreUtils.DrawFullScreen(nativeCmd, m_Material, mpb, 0);
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            if (m_FallbackAtmosphericScatteringLut != null)
            {
                CoreUtils.Destroy(m_FallbackAtmosphericScatteringLut);
                m_FallbackAtmosphericScatteringLut = null;
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

        private static void ConfigureAtmosphericScatteringDescriptor(RenderGraphTexture texture)
        {
            if (texture?.desc == null)
                return;

            texture.desc.Dimension = TextureDimension.Tex3D;
            texture.desc.Slices = AtmosphereLUTPass.AtmosphericScatteringDepth;
            texture.desc.FilterMode = FilterMode.Bilinear;
            texture.desc.WrapMode = TextureWrapMode.Clamp;
            texture.desc.UseMipMap = false;
            texture.desc.AutoGenerateMips = false;
            texture.desc.MipCount = 1;
        }

        private void EnsureFallbackAtmosphericScatteringLut()
        {
            if (m_FallbackAtmosphericScatteringLut != null)
                return;

            m_FallbackAtmosphericScatteringLut = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false)
            {
                name = "VividFallbackAtmosphericScatteringLUT",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            m_FallbackAtmosphericScatteringLut.SetPixels(new[] { Color.black });
            m_FallbackAtmosphericScatteringLut.Apply(false, true);
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

        private static bool HasValidAtmosphericScatteringLut(Texture texture)
        {
            if (texture == null
                || texture.dimension != TextureDimension.Tex3D
                || texture.width <= 1
                || texture.height <= 1)
            {
                return false;
            }

            if (texture is RenderTexture renderTexture)
                return renderTexture.volumeDepth > 1;

            return texture is Texture3D texture3D && texture3D.depth > 1;
        }
    }
}
