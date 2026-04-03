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
        VisibilityBuffer = 4,
        AutoExposure = 5,
    }

    public enum OverlayDebugDepthMode
    {
        Raw = 0,
        Linear01 = 1,
    }

    public sealed class OverlayDebugPass : UnsafePass
    {
        internal const string OverlayDebugShaderName = "Hidden/VividRP/OverlayDebug";
        internal const float MinOverlayViewportFraction = 0.35f;
        private const int AutoExposureHistogramBucketCount = 64;
        private const int AutoExposureHistogramThreadGroupSizeX = 8;
        private const int AutoExposureHistogramThreadGroupSizeY = 8;
        private const string ClearHistogramKernelName = "ClearHistogram";
        private const string BuildHistogramKernelName = "BuildHistogram";
        private const int AutoExposureVectorStride = sizeof(float) * 4;

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int DebugTextureId = Shader.PropertyToID("_DebugTexture");
        private static readonly int DebugTextureArrayId = Shader.PropertyToID("_DebugTextureArray");
        private static readonly int DebugVisibilityTextureId = Shader.PropertyToID("_DebugVisibilityTexture");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int DebugTextureScaleBiasId = Shader.PropertyToID("_DebugTextureScaleBias");
        private static readonly int OverlayRectId = Shader.PropertyToID("_OverlayRect");
        private static readonly int OverlayScreenSizeId = Shader.PropertyToID("_OverlayScreenSize");
        private static readonly int DebugTextureAvailableId = Shader.PropertyToID("_DebugTextureAvailable");
        private static readonly int DebugTextureIsArrayId = Shader.PropertyToID("_DebugTextureIsArray");
        private static readonly int DebugSliceId = Shader.PropertyToID("_DebugSlice");
        private static readonly int VisualizationModeId = Shader.PropertyToID("_VisualizationMode");
        private static readonly int DepthModeId = Shader.PropertyToID("_DepthMode");
        private static readonly int DebugExposureId = Shader.PropertyToID("_DebugExposure");
        private static readonly int DebugOpacityId = Shader.PropertyToID("_DebugOpacity");
        private static readonly int AutoExposureDebugHistogramBufferId = Shader.PropertyToID("_AutoExposureHistogramBuffer");
        private static readonly int AutoExposureDebugCurrentExposureBufferId = Shader.PropertyToID("_AutoExposureCurrentExposureBuffer");
        private static readonly int AutoExposureDebugStateId = Shader.PropertyToID("_AutoExposureDebugState");
        private static readonly int AutoExposureDebugHistogramTransformId = Shader.PropertyToID("_AutoExposureHistogramTransform");
        private static readonly int AutoExposureDebugRangeParamsId = Shader.PropertyToID("_AutoExposureRangeParams");
        private static readonly int AutoExposureInputTextureId = Shader.PropertyToID("_InputColor");
        private static readonly int AutoExposureHistogramBufferId = Shader.PropertyToID("_HistogramBuffer");
        private static readonly int AutoExposurePreviousBufferId = Shader.PropertyToID("_PreviousExposureBuffer");
        private static readonly int AutoExposureMeterMaskId = Shader.PropertyToID("_AutoExposureMeterMask");
        private static readonly int AutoExposureParams0Id = Shader.PropertyToID("_AutoExposureParams0");
        private static readonly int AutoExposureParams1Id = Shader.PropertyToID("_AutoExposureParams1");
        private static readonly int AutoExposureParams2Id = Shader.PropertyToID("_AutoExposureParams2");
        private static readonly int AutoExposureParams3Id = Shader.PropertyToID("_AutoExposureParams3");
        private static readonly int AutoExposureScreenSizeId = Shader.PropertyToID("_AutoExposureScreenSize");
        private static readonly uint[] s_ZeroHistogramData = new uint[AutoExposureHistogramBucketCount];
        private static readonly Vector4[] s_FallbackExposureData = new Vector4[1];

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

        private Material m_Material;
        private MaterialPropertyBlock m_MaterialPropertyBlock;
        private ComputeShader m_AutoExposureCompute;
        private int m_ClearHistogramKernel = -1;
        private int m_BuildHistogramKernel = -1;
        private float m_ResolvedOverlayAmount;
        private int m_ResolvedArraySlice;
        private float m_ResolvedExposure;
        private float m_ResolvedOpacity = 1f;
        private OverlayDebugVisualizationMode m_ResolvedVisualizationMode = OverlayDebugVisualizationMode.Auto;
        private OverlayDebugDepthMode m_ResolvedDepthMode = OverlayDebugDepthMode.Raw;
        private Vector4 m_OverlayRect = new(0.65f, 0.65f, MinOverlayViewportFraction, MinOverlayViewportFraction);
        private Vector4 m_OverlayScreenSize = new(1f, 1f, 1f, 1f);
        private AutoExposureSettingsData m_AutoExposureSettings;
        private VividExposureData m_ExposureData;
        private GraphicsBuffer m_AutoExposureHistogramBuffer;
        private GraphicsBuffer m_FallbackExposureBuffer;
        private Vector4 m_AutoExposureDebugState;
        private Vector4 m_AutoExposureHistogramTransform;
        private Vector4 m_AutoExposureRangeParams;
        private int m_AutoExposureWidth = 1;
        private int m_AutoExposureHeight = 1;
        private bool m_AutoExposureHistogramAvailable;

        internal readonly struct OverlayDebugSettingsData
        {
            public readonly float overlayAmount;
            public readonly int arraySlice;
            public readonly float exposure;
            public readonly float opacity;
            public readonly OverlayDebugVisualizationMode visualizationMode;
            public readonly OverlayDebugDepthMode depthMode;

            public OverlayDebugSettingsData(
                float overlayAmount,
                int arraySlice,
                float exposure,
                float opacity,
                OverlayDebugVisualizationMode visualizationMode,
                OverlayDebugDepthMode depthMode)
            {
                this.overlayAmount = overlayAmount;
                this.arraySlice = arraySlice;
                this.exposure = exposure;
                this.opacity = opacity;
                this.visualizationMode = visualizationMode;
                this.depthMode = depthMode;
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
            set => m_VisualizationMode = value;
        }

        public OverlayDebugDepthMode DepthMode
        {
            get => m_DepthMode;
            set => m_DepthMode = value;
        }

        public OverlayDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(OverlayDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_DebugTexture = RenderGraphTexture.CreateInput("DebugTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture = RenderGraphTexture.CreateInput("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.OverlayDebugShader;
            shader ??= Shader.Find(OverlayDebugShaderName);
            m_AutoExposureCompute = resources?.AutoExposureCompute;
            if (m_AutoExposureCompute != null)
            {
                m_ClearHistogramKernel = m_AutoExposureCompute.FindKernel(ClearHistogramKernelName);
                m_BuildHistogramKernel = m_AutoExposureCompute.FindKernel(BuildHistogramKernelName);
            }

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{OverlayDebugShaderName}' for {nameof(OverlayDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            EnsureAutoExposureDebugBuffers();
        }

        public override void Prepare(ContextContainer frameData)
        {
            var resolvedSettings = ResolveSettings(
                m_OverlayAmount,
                m_ArraySlice,
                m_Exposure,
                m_Opacity,
                m_VisualizationMode,
                m_DepthMode,
                VividVolumeManagerUtility.GetOverlayDebugVolume());

            m_ResolvedOverlayAmount = resolvedSettings.overlayAmount;
            m_ResolvedArraySlice = resolvedSettings.arraySlice;
            m_ResolvedExposure = resolvedSettings.exposure;
            m_ResolvedOpacity = resolvedSettings.opacity;
            m_ResolvedVisualizationMode = resolvedSettings.visualizationMode;
            m_ResolvedDepthMode = resolvedSettings.depthMode;

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            var width = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            var height = ResolveOutputDimension(
                descriptor => descriptor.Height,
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

            m_ExposureData = frameData.Get<VividExposureData>();
            m_AutoExposureSettings = m_ExposureData != null
                ? m_ExposureData.settings
                : AutoExposureSettingsData.CreateDefault();
            m_AutoExposureWidth = ResolveOutputDimension(
                descriptor => descriptor.Width,
                cameraData.actualWidth,
                cameraData.pixelWidth,
                Screen.width,
                m_SourceTexture?.desc);
            m_AutoExposureHeight = ResolveOutputDimension(
                descriptor => descriptor.Height,
                cameraData.actualHeight,
                cameraData.pixelHeight,
                Screen.height,
                m_SourceTexture?.desc);

            var exposureEnabled = m_ExposureData != null && m_ExposureData.exposureEnabled;
            var histogramMode = m_AutoExposureSettings.mode == AutoExposureMode.Histogram;
            m_AutoExposureHistogramAvailable = m_ResolvedVisualizationMode == OverlayDebugVisualizationMode.AutoExposure
                && exposureEnabled
                && histogramMode
                && m_AutoExposureCompute != null
                && m_ClearHistogramKernel >= 0
                && m_BuildHistogramKernel >= 0;
            m_AutoExposureDebugState = new Vector4(
                exposureEnabled ? 1f : 0f,
                histogramMode ? 1f : 0f,
                m_AutoExposureHistogramAvailable ? 1f : 0f,
                m_ExposureData != null && m_ExposureData.hasValidHistory ? 1f : 0f);
            m_AutoExposureHistogramTransform = new Vector4(
                m_AutoExposureSettings.histogramScale,
                m_AutoExposureSettings.histogramBias,
                m_AutoExposureSettings.luminanceMin,
                m_AutoExposureSettings.exposureCompensation);
            m_AutoExposureRangeParams = new Vector4(
                m_AutoExposureSettings.minAverageLuminance,
                m_AutoExposureSettings.maxAverageLuminance,
                m_AutoExposureSettings.exposureLowPercent,
                m_AutoExposureSettings.exposureHighPercent);
        }

        public override void Record(UnsafeGraphContext context)
        {
            if (m_Material == null
                || !m_SourceTexture.innerHandle.IsValid()
                || !m_OutputTexture.innerHandle.IsValid())
            {
                return;
            }

            var cmd = context.cmd;
            var nativeCmd = CommandBufferHelpers.GetNativeCommandBuffer(cmd);
            var sourceTexture = ResolveTexture(m_SourceTexture.innerHandle);
            if (sourceTexture == null)
                return;

            var debugTexture = m_DebugTexture != null && m_DebugTexture.innerHandle.IsValid()
                ? ResolveTexture(m_DebugTexture.innerHandle)
                : null;
            var isDebugTextureArray = IsTextureArray(m_DebugTexture?.desc, debugTexture);
            var resolvedSlice = isDebugTextureArray
                ? ResolveSliceIndex(m_ResolvedArraySlice, ResolveTextureSliceCount(m_DebugTexture?.desc, debugTexture))
                : 0;
            var resolvedVisualizationMode = ResolveVisualizationMode(
                m_ResolvedVisualizationMode,
                m_DebugTexture?.desc,
                debugTexture);
            var usesAutoExposureVisualization = resolvedVisualizationMode == OverlayDebugVisualizationMode.AutoExposure;
            var debugTextureScaleBias = m_DebugTexture != null && m_DebugTexture.innerHandle.IsValid()
                ? GetScaleBias(m_DebugTexture.innerHandle)
                : new Vector4(1f, 1f, 0f, 0f);
            if (usesAutoExposureVisualization)
                ExecuteAutoExposureDebugHistogram(cmd);

            EnsureAutoExposureDebugBuffers();
            var currentExposureBuffer = ResolveCurrentExposureBuffer();
            m_Material.SetBuffer(AutoExposureDebugHistogramBufferId, m_AutoExposureHistogramBuffer);
            m_Material.SetBuffer(AutoExposureDebugCurrentExposureBufferId, currentExposureBuffer);

            m_MaterialPropertyBlock ??= new MaterialPropertyBlock();
            var mpb = m_MaterialPropertyBlock;
            mpb.Clear();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetVector(SourceTextureScaleBiasId, GetScaleBias(m_SourceTexture.innerHandle));
            mpb.SetVector(DebugTextureScaleBiasId, debugTextureScaleBias);
            mpb.SetVector(OverlayRectId, m_OverlayRect);
            mpb.SetVector(OverlayScreenSizeId, m_OverlayScreenSize);
            mpb.SetInt(DebugTextureAvailableId, debugTexture != null || usesAutoExposureVisualization ? 1 : 0);
            mpb.SetInt(DebugTextureIsArrayId, isDebugTextureArray ? 1 : 0);
            mpb.SetInt(DebugSliceId, resolvedSlice);
            mpb.SetInt(VisualizationModeId, (int)resolvedVisualizationMode);
            mpb.SetInt(DepthModeId, (int)m_ResolvedDepthMode);
            mpb.SetFloat(DebugExposureId, m_ResolvedExposure);
            mpb.SetFloat(DebugOpacityId, m_ResolvedOpacity);
            mpb.SetVector(AutoExposureDebugStateId, m_AutoExposureDebugState);
            mpb.SetVector(AutoExposureDebugHistogramTransformId, m_AutoExposureHistogramTransform);
            mpb.SetVector(AutoExposureDebugRangeParamsId, m_AutoExposureRangeParams);
            mpb.SetTexture(DebugTextureId, debugTexture != null && !isDebugTextureArray ? debugTexture : Texture2D.blackTexture);
            mpb.SetTexture(DebugVisibilityTextureId, debugTexture != null && !isDebugTextureArray ? debugTexture : Texture2D.blackTexture);

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

            m_AutoExposureHistogramBuffer?.Dispose();
            m_AutoExposureHistogramBuffer = null;

            m_FallbackExposureBuffer?.Dispose();
            m_FallbackExposureBuffer = null;
            m_MaterialPropertyBlock = null;

            m_AutoExposureCompute = null;
            m_ClearHistogramKernel = -1;
            m_BuildHistogramKernel = -1;
        }

        internal static OverlayDebugSettingsData ResolveSettings(
            float fallbackOverlayAmount,
            float fallbackArraySlice,
            float fallbackExposure,
            float fallbackOpacity,
            OverlayDebugVisualizationMode fallbackVisualizationMode,
            OverlayDebugDepthMode fallbackDepthMode,
            OverlayDebugVolume volume)
        {
            var overlayAmount = Mathf.Clamp01(fallbackOverlayAmount);
            var arraySlice = Mathf.Max(0, Mathf.RoundToInt(fallbackArraySlice));
            var exposure = Mathf.Clamp(fallbackExposure, -16f, 16f);
            var opacity = Mathf.Clamp01(fallbackOpacity);
            var visualizationMode = fallbackVisualizationMode;
            var depthMode = fallbackDepthMode;

            if (volume == null || !volume.active)
            {
                return new OverlayDebugSettingsData(overlayAmount, arraySlice, exposure, opacity, visualizationMode, depthMode);
            }

            if (volume.overlayAmount != null && volume.overlayAmount.overrideState)
                overlayAmount = Mathf.Clamp01(volume.overlayAmount.value);

            if (volume.arraySlice != null && volume.arraySlice.overrideState)
                arraySlice = Mathf.Max(0, volume.arraySlice.value);

            if (volume.exposure != null && volume.exposure.overrideState)
                exposure = Mathf.Clamp(volume.exposure.value, -16f, 16f);

            if (volume.opacity != null && volume.opacity.overrideState)
                opacity = Mathf.Clamp01(volume.opacity.value);

            if (volume.visualizationMode != null && volume.visualizationMode.overrideState)
                visualizationMode = volume.visualizationMode.value;

            if (volume.depthMode != null && volume.depthMode.overrideState)
                depthMode = volume.depthMode.value;

            return new OverlayDebugSettingsData(overlayAmount, arraySlice, exposure, opacity, visualizationMode, depthMode);
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
                return OverlayDebugVisualizationMode.VisibilityBuffer;

            var componentCount = GraphicsFormatUtility.GetComponentCount(format);
            if (componentCount <= 1)
                return OverlayDebugVisualizationMode.Depth;

            if (componentCount == 2)
                return OverlayDebugVisualizationMode.MotionVectors;

            return OverlayDebugVisualizationMode.Color;
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

            m_OutputTexture.Resize(width, height);
            m_OutputTexture.desc.ColorFormat = ResolveOutputFormat(sourceDescriptor);
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
            if (HasExplicitSize(m_SourceTexture?.desc))
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

        private void ExecuteAutoExposureDebugHistogram(UnsafeCommandBuffer cmd)
        {
            EnsureAutoExposureDebugBuffers();
            ClearAutoExposureHistogramBuffer(cmd);

            if (!m_AutoExposureHistogramAvailable
                || cmd == null
                || m_AutoExposureCompute == null
                || m_AutoExposureHistogramBuffer == null
                || !m_SourceTexture.innerHandle.IsValid())
            {
                return;
            }

            var meterMask = m_AutoExposureSettings.meterMask != null
                ? m_AutoExposureSettings.meterMask
                : Texture2D.whiteTexture;
            var previousExposureBuffer = ResolvePreviousExposureBuffer();
            if (previousExposureBuffer == null)
                return;

            BindAutoExposureParameters(cmd, m_BuildHistogramKernel);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureInputTextureId, m_SourceTexture.innerHandle);
            cmd.SetComputeTextureParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureMeterMaskId, meterMask);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
            cmd.SetComputeBufferParam(m_AutoExposureCompute, m_BuildHistogramKernel, AutoExposurePreviousBufferId, previousExposureBuffer);
            cmd.DispatchCompute(
                m_AutoExposureCompute,
                m_BuildHistogramKernel,
                CoreUtils.DivRoundUp(m_AutoExposureWidth, AutoExposureHistogramThreadGroupSizeX),
                CoreUtils.DivRoundUp(m_AutoExposureHeight, AutoExposureHistogramThreadGroupSizeY),
                1);
        }

        private void BindAutoExposureParameters(UnsafeCommandBuffer cmd, int kernel)
        {
            if (cmd == null || kernel < 0 || m_AutoExposureCompute == null)
                return;

            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams0Id,
                new Vector4(
                    m_AutoExposureSettings.exposureLowPercent,
                    m_AutoExposureSettings.exposureHighPercent,
                    m_AutoExposureSettings.minAverageLuminance,
                    m_AutoExposureSettings.maxAverageLuminance));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams1Id,
                new Vector4(
                    m_AutoExposureSettings.exposureSpeedUp,
                    m_AutoExposureSettings.exposureSpeedDown,
                    m_AutoExposureSettings.exposureCompensation,
                    m_AutoExposureSettings.deltaTime));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams2Id,
                new Vector4(
                    m_AutoExposureSettings.histogramScale,
                    m_AutoExposureSettings.histogramBias,
                    m_AutoExposureSettings.luminanceMin,
                    m_AutoExposureSettings.forceTarget));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureParams3Id,
                new Vector4(
                    m_AutoExposureSettings.exponentialUpM,
                    m_AutoExposureSettings.exponentialDownM,
                    m_AutoExposureSettings.startDistance,
                    0f));
            cmd.SetComputeVectorParam(
                m_AutoExposureCompute,
                AutoExposureScreenSizeId,
                new Vector4(
                    m_AutoExposureWidth,
                    m_AutoExposureHeight,
                    1f / Mathf.Max(1, m_AutoExposureWidth),
                    1f / Mathf.Max(1, m_AutoExposureHeight)));
        }

        private void EnsureAutoExposureDebugBuffers()
        {
            if (m_AutoExposureHistogramBuffer == null
                || m_AutoExposureHistogramBuffer.count != AutoExposureHistogramBucketCount
                || m_AutoExposureHistogramBuffer.stride != sizeof(uint))
            {
                m_AutoExposureHistogramBuffer?.Dispose();
                m_AutoExposureHistogramBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    AutoExposureHistogramBucketCount,
                    sizeof(uint));
                m_AutoExposureHistogramBuffer.name = "VividRP Overlay Auto Exposure Histogram";
                m_AutoExposureHistogramBuffer.SetData(s_ZeroHistogramData);
            }

            if (m_FallbackExposureBuffer == null
                || m_FallbackExposureBuffer.count != 1
                || m_FallbackExposureBuffer.stride != AutoExposureVectorStride)
            {
                m_FallbackExposureBuffer?.Dispose();
                m_FallbackExposureBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    AutoExposureVectorStride);
                m_FallbackExposureBuffer.name = "VividRP Overlay Auto Exposure Fallback";
                s_FallbackExposureData[0] = new Vector4(1f, 1f, AutoExposureSettingsResolver.MiddleGrey, 1f);
                m_FallbackExposureBuffer.SetData(s_FallbackExposureData);
            }
        }

        private void ClearAutoExposureHistogramBuffer(UnsafeCommandBuffer cmd)
        {
            if (m_AutoExposureHistogramBuffer == null)
                return;

            if (cmd != null
                && m_AutoExposureCompute != null
                && m_ClearHistogramKernel >= 0)
            {
                BindAutoExposureParameters(cmd, m_ClearHistogramKernel);
                cmd.SetComputeBufferParam(m_AutoExposureCompute, m_ClearHistogramKernel, AutoExposureHistogramBufferId, m_AutoExposureHistogramBuffer);
                cmd.DispatchCompute(m_AutoExposureCompute, m_ClearHistogramKernel, 1, 1, 1);
                return;
            }

            m_AutoExposureHistogramBuffer.SetData(s_ZeroHistogramData);
        }

        private GraphicsBuffer ResolveCurrentExposureBuffer()
        {
            return m_ExposureData?.currentExposureBuffer
                   ?? m_ExposureData?.previousExposureBuffer
                   ?? m_ExposureData?.defaultExposureBuffer
                   ?? m_FallbackExposureBuffer;
        }

        private GraphicsBuffer ResolvePreviousExposureBuffer()
        {
            if (m_ExposureData == null)
                return m_FallbackExposureBuffer;

            if (m_ExposureData.hasValidHistory && m_ExposureData.previousExposureBuffer != null)
                return m_ExposureData.previousExposureBuffer;

            return m_ExposureData.defaultExposureBuffer ?? m_FallbackExposureBuffer;
        }

    }
}
