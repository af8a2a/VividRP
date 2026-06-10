using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum OverlayDebugVisualizationMode
    {
        Auto = 0,
        Color = 1,
        Depth = 2,
        MotionVectors = 3,
    }

    public enum OverlayDebugDepthMode
    {
        Raw = 0,
        Linear01 = 1,
    }

    public enum OverlayDebugChannelMode
    {
        RGB = 0,
        Red = 1,
        Green = 2,
        Blue = 3,
        Alpha = 4,
    }

    public sealed class OverlayDebugPass : UnsafePass
    {
        internal const string OverlayDebugShaderName = "Hidden/VividRP/OverlayDebug";
        internal const float MinOverlayViewportFraction = 0.35f;

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int DebugTextureId = Shader.PropertyToID("_DebugTexture");
        private static readonly int DebugTextureArrayId = Shader.PropertyToID("_DebugTextureArray");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int DebugTextureScaleBiasId = Shader.PropertyToID("_DebugTextureScaleBias");
        private static readonly int OverlayRectId = Shader.PropertyToID("_OverlayRect");
        private static readonly int OverlayScreenSizeId = Shader.PropertyToID("_OverlayScreenSize");
        private static readonly int DebugTextureAvailableId = Shader.PropertyToID("_DebugTextureAvailable");
        private static readonly int DebugTextureIsArrayId = Shader.PropertyToID("_DebugTextureIsArray");
        private static readonly int DebugSliceId = Shader.PropertyToID("_DebugSlice");
        private static readonly int VisualizationModeId = Shader.PropertyToID("_VisualizationMode");
        private static readonly int DepthModeId = Shader.PropertyToID("_DepthMode");
        private static readonly int DebugChannelModeId = Shader.PropertyToID("_DebugChannelMode");
        private static readonly int DebugExposureId = Shader.PropertyToID("_DebugExposure");
        private static readonly int DebugOpacityId = Shader.PropertyToID("_DebugOpacity");

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(Name = "DebugTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_DebugTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField, Range(0f, 1f)]
        private float m_OverlayAmount;

        [SerializeField, Min(0f)]
        private float m_ArraySlice;

        [SerializeField, Range(-16f, 16f)]
        private float m_Exposure;

        [SerializeField, Range(0f, 1f)]
        private float m_Opacity = 1f;

        [SerializeField]
        private OverlayDebugVisualizationMode m_VisualizationMode = OverlayDebugVisualizationMode.Auto;

        [SerializeField]
        private OverlayDebugDepthMode m_DepthMode = OverlayDebugDepthMode.Raw;

        [SerializeField]
        private OverlayDebugChannelMode m_ChannelMode = OverlayDebugChannelMode.RGB;

        private Material m_Material;
        private MaterialPropertyBlock m_MaterialPropertyBlock;
        private float m_ResolvedOverlayAmount;
        private int m_ResolvedArraySlice;
        private float m_ResolvedExposure;
        private float m_ResolvedOpacity = 1f;
        private OverlayDebugVisualizationMode m_ResolvedVisualizationMode = OverlayDebugVisualizationMode.Auto;
        private OverlayDebugDepthMode m_ResolvedDepthMode = OverlayDebugDepthMode.Raw;
        private OverlayDebugChannelMode m_ResolvedChannelMode = OverlayDebugChannelMode.RGB;
        private Vector4 m_OverlayRect = new(0.65f, 0.65f, MinOverlayViewportFraction, MinOverlayViewportFraction);
        private Vector4 m_OverlayScreenSize = new(1f, 1f, 1f, 1f);
        private bool m_ShouldSkipExecution;

        internal readonly struct OverlayDebugSettingsData
        {
            public readonly float overlayAmount;
            public readonly int arraySlice;
            public readonly float exposure;
            public readonly float opacity;
            public readonly OverlayDebugVisualizationMode visualizationMode;
            public readonly OverlayDebugDepthMode depthMode;
            public readonly OverlayDebugChannelMode channelMode;

            public OverlayDebugSettingsData(
                float overlayAmount,
                int arraySlice,
                float exposure,
                float opacity,
                OverlayDebugVisualizationMode visualizationMode,
                OverlayDebugDepthMode depthMode,
                OverlayDebugChannelMode channelMode)
            {
                this.overlayAmount = overlayAmount;
                this.arraySlice = arraySlice;
                this.exposure = exposure;
                this.opacity = opacity;
                this.visualizationMode = visualizationMode;
                this.depthMode = depthMode;
                this.channelMode = channelMode;
            }
        }

        public float OverlayAmount
        {
            get => m_OverlayAmount;
            set => m_OverlayAmount = Mathf.Clamp01(value);
        }

        public int ArraySlice
        {
            get => Mathf.Max(0, Mathf.RoundToInt(m_ArraySlice));
            set => m_ArraySlice = Mathf.Max(0, value);
        }

        public float Exposure
        {
            get => m_Exposure;
            set => m_Exposure = Mathf.Clamp(value, -16f, 16f);
        }

        public float Opacity
        {
            get => m_Opacity;
            set => m_Opacity = Mathf.Clamp01(value);
        }

        public OverlayDebugVisualizationMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = NormalizeVisualizationMode(value);
        }

        public OverlayDebugDepthMode DepthMode
        {
            get => m_DepthMode;
            set => m_DepthMode = value;
        }

        public OverlayDebugChannelMode ChannelMode
        {
            get => m_ChannelMode;
            set => m_ChannelMode = NormalizeChannelMode(value);
        }

        public OverlayDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(OverlayDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_DebugTexture = RenderGraphTexture.CreateInput("DebugTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.OverlayDebugShader;
            shader ??= Shader.Find(OverlayDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{OverlayDebugShaderName}' for {nameof(OverlayDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Prepare(ContextContainer frameData)
        {
            var resolvedSettings = ResolveSettings(VividRenderingDebugDisplaySettings.Data);

            m_ResolvedOverlayAmount = resolvedSettings.overlayAmount;
            m_ResolvedArraySlice = resolvedSettings.arraySlice;
            m_ResolvedExposure = resolvedSettings.exposure;
            m_ResolvedOpacity = resolvedSettings.opacity;
            m_ResolvedVisualizationMode = resolvedSettings.visualizationMode;
            m_ResolvedDepthMode = resolvedSettings.depthMode;
            m_ResolvedChannelMode = resolvedSettings.channelMode;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            var width = RenderGraphTextureDescUtility.ResolveMaxExplicitWidth(
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            var height = RenderGraphTextureDescUtility.ResolveMaxExplicitHeight(
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc);

            ConfigureOutputTexture(width, height, GetPreferredSourceDescriptor());

            m_OverlayRect = ResolveOverlayRect(m_ResolvedOverlayAmount);
            m_OverlayScreenSize = new Vector4(
                width,
                height,
                1f / Mathf.Max(1, width),
                1f / Mathf.Max(1, height));

            var textureSliceCount = ResolveTextureSliceCount(m_DebugTexture?.desc, null);
            m_ResolvedArraySlice = ResolveSliceIndex(m_ResolvedArraySlice, textureSliceCount);
            m_ResolvedVisualizationMode = ResolveVisualizationMode(m_ResolvedVisualizationMode, m_DebugTexture?.desc, null);
        }

        public override void Record(UnsafePassContext context)
        {
            if (m_ShouldSkipExecution)
            {
                DebugPassCameraUtility.TryPassThrough(context, m_SourceTexture, m_OutputTexture);
                return;
            }

            if (m_Material == null
                || !m_SourceTexture.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
            var sourceTexture = TextureResolveUtility.ResolveTexture(m_SourceTexture.innerHandle);
            if (sourceTexture == null)
                return;

            var debugTexture = m_DebugTexture != null && m_DebugTexture.innerHandle.IsValid()
                ? TextureResolveUtility.ResolveTexture(m_DebugTexture.innerHandle)
                : null;
            var isDebugTextureArray = IsTextureArray(m_DebugTexture?.desc, debugTexture);
            var resolvedSlice = isDebugTextureArray
                ? ResolveSliceIndex(m_ResolvedArraySlice, ResolveTextureSliceCount(m_DebugTexture?.desc, debugTexture))
                : 0;
            var resolvedVisualizationMode = ResolveVisualizationMode(
                m_ResolvedVisualizationMode,
                m_DebugTexture?.desc,
                debugTexture);
            var debugTextureScaleBias = m_DebugTexture != null && m_DebugTexture.innerHandle.IsValid()
                ? TextureScaleBiasUtility.GetScaleBias(m_DebugTexture.innerHandle)
                : new Vector4(1f, 1f, 0f, 0f);

            m_MaterialPropertyBlock ??= new MaterialPropertyBlock();
            var mpb = m_MaterialPropertyBlock;
            mpb.Clear();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetVector(SourceTextureScaleBiasId, TextureScaleBiasUtility.GetScaleBias(m_SourceTexture.innerHandle));
            mpb.SetVector(DebugTextureScaleBiasId, debugTextureScaleBias);
            mpb.SetVector(OverlayRectId, m_OverlayRect);
            mpb.SetVector(OverlayScreenSizeId, m_OverlayScreenSize);
            mpb.SetInt(DebugTextureAvailableId, debugTexture != null ? 1 : 0);
            mpb.SetInt(DebugTextureIsArrayId, isDebugTextureArray ? 1 : 0);
            mpb.SetInt(DebugSliceId, resolvedSlice);
            mpb.SetInt(VisualizationModeId, (int)resolvedVisualizationMode);
            mpb.SetInt(DepthModeId, (int)m_ResolvedDepthMode);
            mpb.SetInt(DebugChannelModeId, (int)m_ResolvedChannelMode);
            mpb.SetFloat(DebugExposureId, m_ResolvedExposure);
            mpb.SetFloat(DebugOpacityId, m_ResolvedOpacity);
            mpb.SetTexture(DebugTextureId, debugTexture != null && !isDebugTextureArray ? debugTexture : Texture2D.blackTexture);
            if (debugTexture != null && isDebugTextureArray)
                mpb.SetTexture(DebugTextureArrayId, debugTexture);

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

            m_MaterialPropertyBlock = null;
            m_ShouldSkipExecution = false;
        }

        internal static OverlayDebugSettingsData ResolveSettings(VividRenderingDebugSettingsData data)
        {
            var overlayAmount = 0f;
            var arraySlice = 0;
            var exposure = 0f;
            var opacity = 1f;
            var visualizationMode = OverlayDebugVisualizationMode.Auto;
            var depthMode = OverlayDebugDepthMode.Raw;
            var channelMode = OverlayDebugChannelMode.RGB;

            if (data == null)
            {
                return new OverlayDebugSettingsData(
                    overlayAmount,
                    arraySlice,
                    exposure,
                    opacity,
                    visualizationMode,
                    depthMode,
                    channelMode);
            }

            overlayAmount = Mathf.Clamp01(data.overlayAmount);
            arraySlice = Mathf.Max(0, data.arraySlice);
            exposure = Mathf.Clamp(data.overlayExposure, -16f, 16f);
            opacity = Mathf.Clamp01(data.overlayOpacity);
            visualizationMode = NormalizeVisualizationMode(data.visualizationMode);
            depthMode = data.depthMode;
            channelMode = NormalizeChannelMode(data.channelMode);

            return new OverlayDebugSettingsData(
                overlayAmount,
                arraySlice,
                exposure,
                opacity,
                visualizationMode,
                depthMode,
                channelMode);
        }

        internal static Vector4 ResolveOverlayRect(float overlayAmount)
        {
            var normalizedOverlayAmount = Mathf.Clamp01(overlayAmount);
            var size = Mathf.Lerp(MinOverlayViewportFraction, 1f, normalizedOverlayAmount);
            return new Vector4(1f - size, 1f - size, size, size);
        }

        internal static OverlayDebugVisualizationMode ResolveVisualizationMode(
            OverlayDebugVisualizationMode requestedMode,
            RenderGraphTextureDesc descriptor,
            Texture texture)
        {
            requestedMode = NormalizeVisualizationMode(requestedMode);

            if (requestedMode != OverlayDebugVisualizationMode.Auto)
                return requestedMode;

            if (descriptor != null && descriptor.DepthBufferBits != DepthBits.None)
                return OverlayDebugVisualizationMode.Depth;

            var format = ResolveGraphicsFormat(descriptor, texture);
            if (format == GraphicsFormat.None)
                return OverlayDebugVisualizationMode.Color;

            if (GraphicsFormatUtility.IsDepthFormat(format))
                return OverlayDebugVisualizationMode.Depth;

            if (format == GraphicsFormat.R32G32_UInt)
                return OverlayDebugVisualizationMode.Color;

            var componentCount = GraphicsFormatUtility.GetComponentCount(format);
            if (componentCount <= 1)
                return OverlayDebugVisualizationMode.Depth;

            if (componentCount == 2)
                return OverlayDebugVisualizationMode.MotionVectors;

            return OverlayDebugVisualizationMode.Color;
        }

        internal static OverlayDebugVisualizationMode NormalizeVisualizationMode(OverlayDebugVisualizationMode value)
        {
            return value switch
            {
                OverlayDebugVisualizationMode.Auto => OverlayDebugVisualizationMode.Auto,
                OverlayDebugVisualizationMode.Color => OverlayDebugVisualizationMode.Color,
                OverlayDebugVisualizationMode.Depth => OverlayDebugVisualizationMode.Depth,
                OverlayDebugVisualizationMode.MotionVectors => OverlayDebugVisualizationMode.MotionVectors,
                _ => OverlayDebugVisualizationMode.Auto,
            };
        }

        internal static OverlayDebugChannelMode NormalizeChannelMode(OverlayDebugChannelMode value)
        {
            return value switch
            {
                OverlayDebugChannelMode.RGB => OverlayDebugChannelMode.RGB,
                OverlayDebugChannelMode.Red => OverlayDebugChannelMode.Red,
                OverlayDebugChannelMode.Green => OverlayDebugChannelMode.Green,
                OverlayDebugChannelMode.Blue => OverlayDebugChannelMode.Blue,
                OverlayDebugChannelMode.Alpha => OverlayDebugChannelMode.Alpha,
                _ => OverlayDebugChannelMode.RGB,
            };
        }

        internal static int ResolveSliceIndex(int requestedSlice, int sliceCount)
        {
            var maxSliceIndex = Mathf.Max(0, sliceCount - 1);
            return Mathf.Clamp(requestedSlice, 0, maxSliceIndex);
        }

        internal static int ResolveTextureSliceCount(RenderGraphTextureDesc descriptor, Texture texture)
        {
            if (texture is Texture2DArray textureArray)
                return Mathf.Max(1, textureArray.depth);

            if (texture is RenderTexture renderTexture && renderTexture.dimension == TextureDimension.Tex2DArray)
                return Mathf.Max(1, renderTexture.volumeDepth);

            if (descriptor != null && descriptor.Dimension == TextureDimension.Tex2DArray)
                return Mathf.Max(1, descriptor.Slices);

            return 1;
        }

        private void ConfigureOutputTexture(int width, int height, RenderGraphTextureDesc sourceDescriptor)
        {
            if (m_OutputTexture?.desc == null)
                return;

            m_OutputTexture.desc.Width = width;
            m_OutputTexture.desc.Height = height;
            m_OutputTexture.desc.ColorFormat = RenderGraphTextureDescUtility.ResolveColorFormat(sourceDescriptor);
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
            if (RenderGraphTextureDescUtility.HasExplicitSize(m_SourceTexture?.desc))
                return m_SourceTexture.desc;

            return m_SourceTexture?.desc;
        }

        private static bool IsTextureArray(RenderGraphTextureDesc descriptor, Texture texture)
        {
            if (texture != null && texture.dimension == TextureDimension.Tex2DArray)
                return true;

            return descriptor != null && descriptor.Dimension == TextureDimension.Tex2DArray;
        }

        private static GraphicsFormat ResolveGraphicsFormat(RenderGraphTextureDesc descriptor, Texture texture)
        {
            if (descriptor != null && descriptor.ColorFormat != GraphicsFormat.None)
                return descriptor.ColorFormat;

            if (texture == null)
                return GraphicsFormat.None;

            return texture.graphicsFormat;
        }

    }
}
