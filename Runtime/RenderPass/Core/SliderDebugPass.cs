using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public class SliderDebugPass : RasterPass
    {
        internal const string SliderDebugShaderName = "Hidden/VividRP/SliderDebug";

        private static readonly int LeftTextureId = Shader.PropertyToID("_LeftTexture");
        private static readonly int RightTextureId = Shader.PropertyToID("_RightTexture");
        private static readonly int LeftTextureScaleBiasId = Shader.PropertyToID("_LeftTextureScaleBias");
        private static readonly int RightTextureScaleBiasId = Shader.PropertyToID("_RightTextureScaleBias");
        private static readonly int SplitId = Shader.PropertyToID("_Split");

        [RenderGraphResource(Name = "LeftTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_LeftTexture;

        [RenderGraphResource(Name = "RightTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_RightTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField, Range(0f, 100f)]
        private float m_Slider = 50f;

        private Material m_Material;
        private float m_ResolvedSlider = 50f;

        public float Slider
        {
            get => m_Slider;
            set => m_Slider = Mathf.Clamp(value, 0f, 100f);
        }

        public SliderDebugPass()
        {
            m_LeftTexture = RenderGraphTexture.CreateInput("LeftTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_RightTexture = RenderGraphTexture.CreateInput("RightTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.SliderDebugShader;
            shader ??= Shader.Find(SliderDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{SliderDebugShaderName}' for {nameof(SliderDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_Slider = Mathf.Clamp(m_Slider, 0f, 100f);
            m_ResolvedSlider = ResolveSliderValue(m_Slider, VividVolumeManagerUtility.GetSliderDebugVolume());

            var cameraData = frameData.Get<VividCameraData>();
            var width = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_LeftTexture?.desc,
                m_RightTexture?.desc);
            var height = ResolveOutputDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_LeftTexture?.desc,
                m_RightTexture?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());
        }

        public override void Record(RasterGraphContext context)
        {
            if (m_Material == null
                || !m_LeftTexture.innerHandle.IsValid()
                || !m_RightTexture.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var leftTexture = ResolveTexture(m_LeftTexture.innerHandle);
            var rightTexture = ResolveTexture(m_RightTexture.innerHandle);
            if (leftTexture == null || rightTexture == null)
                return;

            var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
            mpb.SetTexture(LeftTextureId, leftTexture);
            mpb.SetTexture(RightTextureId, rightTexture);
            mpb.SetVector(LeftTextureScaleBiasId, GetScaleBias(m_LeftTexture.innerHandle));
            mpb.SetVector(RightTextureScaleBiasId, GetScaleBias(m_RightTexture.innerHandle));
            mpb.SetFloat(SplitId, m_ResolvedSlider * 0.01f);

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
            m_OutputTexture.desc.ColorFormat = ResolveOutputFormat(sourceDescriptor);
            m_OutputTexture.desc.DepthBufferBits = DepthBits.None;
            m_OutputTexture.desc.MsaaSamples = MSAASamples.None;
            m_OutputTexture.desc.FilterMode = sourceDescriptor?.FilterMode ?? FilterMode.Bilinear;
            m_OutputTexture.desc.WrapMode = sourceDescriptor?.WrapMode ?? TextureWrapMode.Clamp;
            m_OutputTexture.desc.ClearBuffer = true;
            m_OutputTexture.desc.ClearColor = Color.black;
            m_OutputTexture.desc.UseMipMap = false;
            m_OutputTexture.desc.AutoGenerateMips = false;
            m_OutputTexture.desc.MipCount = 1;
            m_OutputTexture.desc.EnableRandomWrite = false;
            m_OutputTexture.desc.BindTextureMS = false;
            m_OutputTexture.desc.Name = "OutputTexture";

            if (sourceDescriptor == null)
                return;

            m_OutputTexture.desc.Dimension = sourceDescriptor.Dimension;
            m_OutputTexture.desc.Slices = Mathf.Max(1, sourceDescriptor.Slices);
            m_OutputTexture.desc.UseDynamicScale = sourceDescriptor.UseDynamicScale;
            m_OutputTexture.desc.UseDynamicScaleExplicit = sourceDescriptor.UseDynamicScaleExplicit;
            m_OutputTexture.desc.ScaleFactor = sourceDescriptor.ScaleFactor;
        }

        private RenderGraphTextureDesc GetPreferredSourceDescriptor()
        {
            if (HasExplicitSize(m_LeftTexture?.desc))
                return m_LeftTexture.desc;

            if (HasExplicitSize(m_RightTexture?.desc))
                return m_RightTexture.desc;

            return m_LeftTexture?.desc ?? m_RightTexture?.desc;
        }

        private static int ResolveOutputDimension(
            System.Func<RenderGraphTextureDesc, int> selector,
            int actualCameraDimension,
            int cameraDimension,
            int screenDimension,
            params RenderGraphTextureDesc[] descriptors)
        {
            var resolved = 0;

            for (var i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (!HasExplicitSize(descriptor))
                    continue;

                resolved = Mathf.Max(resolved, selector(descriptor));
            }

            if (resolved > 0)
                return resolved;

            return ResolveCameraDimension(actualCameraDimension, cameraDimension, screenDimension);
        }

        private static GraphicsFormat ResolveOutputFormat(RenderGraphTextureDesc sourceDescriptor)
        {
            if (sourceDescriptor != null && sourceDescriptor.ColorFormat != GraphicsFormat.None)
                return sourceDescriptor.ColorFormat;

            return GraphicsFormat.R8G8B8A8_UNorm;
        }

        internal static float ResolveSliderValue(float fallbackSlider, SliderDebugVolume volume)
        {
            var slider = Mathf.Clamp(fallbackSlider, 0f, 100f);
            if (volume == null || !volume.active || volume.slider == null || !volume.slider.overrideState)
                return slider;

            return Mathf.Clamp(volume.slider.value, 0f, 100f);
        }

        private static Texture ResolveTexture(RTHandle handle)
        {
            if (handle == null)
                return null;

            if (handle.rt != null)
                return handle.rt;

            return handle.externalTexture;
        }

        private static bool HasExplicitSize(RenderGraphTextureDesc descriptor)
        {
            return descriptor != null
                && descriptor.Width > 0
                && descriptor.Height > 0
                && !(descriptor.Width == 1 && descriptor.Height == 1);
        }

        private static int ResolveCameraDimension(int actualCameraDimension, int cameraDimension, int screenDimension)
        {
            if (actualCameraDimension > 0)
                return actualCameraDimension;

            if (cameraDimension > 0)
                return cameraDimension;

            return Mathf.Max(1, screenDimension);
        }

        private static Vector4 GetScaleBias(RTHandle handle)
        {
            if (handle == null || !handle.useScaling)
                return new Vector4(1f, 1f, 0f, 0f);

            var scale = handle.rtHandleProperties.rtHandleScale;
            return new Vector4(scale.x, scale.y, 0f, 0f);
        }

    }
}
