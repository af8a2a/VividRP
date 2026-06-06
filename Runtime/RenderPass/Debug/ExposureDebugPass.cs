using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderPass.Core
{
    public enum ExposureDebugMode
    {
        None = 0,
        [InspectorName("Scene EV100 Values")]
        SceneEV100Values = 1,
        [InspectorName("Metering Weighted")]
        MeteringWeighted = 2,
        [InspectorName("Histogram View")]
        HistogramView = 3,
    }

    public sealed class ExposureDebugPass : UnsafePass
    {
        internal const string ExposureDebugShaderName = "Hidden/VividRP/Debug/Exposure";
        private const int HistogramBucketCount = 64;

        private static readonly int SourceTextureId = Shader.PropertyToID("_SourceTexture");
        private static readonly int AutoExposureMeterMaskId = Shader.PropertyToID("_AutoExposureMeterMask");
        private static readonly int AutoExposureHistogramBufferId = Shader.PropertyToID("_AutoExposureHistogramBuffer");
        private static readonly int AutoExposureCurrentExposureBufferId = Shader.PropertyToID("_AutoExposureCurrentExposureBuffer");
        private static readonly int SourceTextureScaleBiasId = Shader.PropertyToID("_SourceTextureScaleBias");
        private static readonly int ExposureDebugStateId = Shader.PropertyToID("_ExposureDebugState");
        private static readonly int ExposureDebugViewParamsId = Shader.PropertyToID("_ExposureDebugViewParams");
        private static readonly int ExposureDebugRangeParamsId = Shader.PropertyToID("_ExposureDebugRangeParams");
        private static readonly int ExposureDebugHistogramTransformId = Shader.PropertyToID("_ExposureDebugHistogramTransform");
        private static readonly int ExposureDebugMeteringParamsId = Shader.PropertyToID("_ExposureDebugMeteringParams");
        private static readonly int MousePixelCoordId = Shader.PropertyToID("_MousePixelCoord");
        private static readonly int DebugTonemapModeId = Shader.PropertyToID("_DebugTonemapMode");
        private static readonly int LogLut3DId = Shader.PropertyToID("_LogLut3D");
        private static readonly int LogLut3DParamsId = Shader.PropertyToID("_LogLut3D_Params");
        private static readonly int CustomToneCurveId = Shader.PropertyToID("_CustomToneCurve");
        private static readonly int ToeSegmentAId = Shader.PropertyToID("_ToeSegmentA");
        private static readonly int ToeSegmentBId = Shader.PropertyToID("_ToeSegmentB");
        private static readonly int MidSegmentAId = Shader.PropertyToID("_MidSegmentA");
        private static readonly int MidSegmentBId = Shader.PropertyToID("_MidSegmentB");
        private static readonly int ShoSegmentAId = Shader.PropertyToID("_ShoSegmentA");
        private static readonly int ShoSegmentBId = Shader.PropertyToID("_ShoSegmentB");
        private static readonly uint[] s_ZeroHistogramData = new uint[HistogramBucketCount];

        [RenderGraphResource(Name = "SourceTexture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_SourceTexture;

        [RenderGraphResource(
            Name = "OutputTexture",
            Access = AccessFlags.Write,
            AttachmentIndex = 0,
            BindingMode = RenderGraphResourceBindingMode.PassOwnedOverrideable)]
        private RenderGraphTexture m_OutputTexture;

        [SerializeField, Range(-16f, 16f)]
        private float m_DebugExposure;

        [SerializeField]
        private ExposureDebugMode m_Mode = ExposureDebugMode.None;

        private Material m_Material;
        private MaterialPropertyBlock m_MaterialPropertyBlock;
        private GraphicsBuffer m_ZeroHistogramBuffer;
        private VividExposureData m_ExposureData;
        private ColorGradingSettingsData m_ColorGradingSettings;
        private Camera m_Camera;
        private ExposureDebugSettingsData m_ResolvedSettings;
        private Vector4 m_SourceTextureScaleBias = new(1f, 1f, 0f, 0f);
        private Vector4 m_ExposureDebugState;
        private Vector4 m_ExposureDebugViewParams;
        private Vector4 m_ExposureDebugRangeParams;
        private Vector4 m_ExposureDebugHistogramTransform;
        private Vector4 m_ExposureDebugMeteringParams;
        private Vector4 m_MousePixelCoord;
        private Vector4 m_LogLut3DParams;
        private Texture m_ExternalLut;
        private bool m_ShouldSkipExecution;

        internal readonly struct ExposureDebugSettingsData
        {
            public readonly float debugExposure;
            public readonly ExposureDebugMode mode;
            public readonly bool centerHistogramAroundMiddleGrey;
            public readonly bool showTonemapCurveAlongHistogramView;
            public readonly bool displayMaskOnly;
            public readonly bool displayOnSceneOverlay;

            public ExposureDebugSettingsData(
                float debugExposure,
                ExposureDebugMode mode,
                bool centerHistogramAroundMiddleGrey,
                bool showTonemapCurveAlongHistogramView,
                bool displayMaskOnly,
                bool displayOnSceneOverlay)
            {
                this.debugExposure = debugExposure;
                this.mode = mode;
                this.centerHistogramAroundMiddleGrey = centerHistogramAroundMiddleGrey;
                this.showTonemapCurveAlongHistogramView = showTonemapCurveAlongHistogramView;
                this.displayMaskOnly = displayMaskOnly;
                this.displayOnSceneOverlay = displayOnSceneOverlay;
            }
        }

        public float DebugExposure
        {
            get => m_DebugExposure;
            set => m_DebugExposure = Mathf.Clamp(value, -16f, 16f);
        }

        public ExposureDebugMode Mode
        {
            get => m_Mode;
            set => m_Mode = value;
        }

        public ExposureDebugPass()
        {
            profilingSampler = new ProfilingSampler(nameof(ExposureDebugPass));

            m_SourceTexture = RenderGraphTexture.CreateInput("SourceTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture = RenderGraphTexture.CreateColorTarget("OutputTexture", GraphicsFormat.R8G8B8A8_UNorm);
            m_OutputTexture.desc.ClearBuffer = false;
        }

        public override void Create()
        {
            var resources = PipelineResourceManager.Get<VividRPCoreResources>();
            var shader = resources?.ExposureDebugShader;
            shader ??= Shader.Find(ExposureDebugShaderName);

            if (shader == null)
            {
                Debug.LogWarning($"[VividRP] Could not find shader '{ExposureDebugShaderName}' for {nameof(ExposureDebugPass)}.");
                return;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            EnsureFallbackHistogramBuffer();
        }

        public override void Prepare(ContextContainer frameData)
        {
            m_ResolvedSettings = ResolveSettings(VividRenderingDebugDisplaySettings.Data);

            var cameraData = frameData.GetOrCreate<VividCameraData>();
            m_ShouldSkipExecution = DebugPassCameraUtility.ShouldSkipExecution(cameraData);
            m_Camera = cameraData.camera;
            m_ExposureData = frameData.Get<VividExposureData>();
            m_ColorGradingSettings = ColorGradingSettingsResolver.Resolve();

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

            m_SourceTextureScaleBias = TextureScaleBiasUtility.GetScaleBias(ResolveHandle(m_SourceTexture));

            var exposureSettings = m_ExposureData != null
                ? m_ExposureData.settings
                : AutoExposureSettingsData.CreateDefault();
            var histogramAvailable = m_ExposureData != null
                && m_ExposureData.autoExposureEnabled
                && m_ExposureData.histogramBuffer != null;

            m_ExposureDebugState = new Vector4(
                m_ExposureData != null && m_ExposureData.exposureEnabled ? 1f : 0f,
                histogramAvailable ? 1f : 0f,
                exposureSettings.meterMask != null ? 1f : 0f,
                m_ExposureData != null && m_ExposureData.autoExposureEnabled ? 1f : 0f);
            m_ExposureDebugViewParams = new Vector4(
                m_ResolvedSettings.debugExposure,
                m_ResolvedSettings.centerHistogramAroundMiddleGrey ? 1f : 0f,
                m_ResolvedSettings.showTonemapCurveAlongHistogramView ? 1f : 0f,
                m_ResolvedSettings.displayOnSceneOverlay ? 1f : 0f);
            m_ExposureDebugRangeParams = new Vector4(
                AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(exposureSettings.minAverageLuminance),
                AutoExposureSettingsResolver.ResolveAverageSceneEV100FromLuminance(exposureSettings.maxAverageLuminance),
                exposureSettings.exposureLowPercent,
                exposureSettings.exposureHighPercent);
            m_ExposureDebugHistogramTransform = ResolveAutoExposureDebugHistogramTransform(exposureSettings);
            m_ExposureDebugMeteringParams = new Vector4(
                m_ResolvedSettings.displayMaskOnly ? 1f : 0f,
                (float)exposureSettings.meteringMode,
                0f,
                0f);
            m_MousePixelCoord = ResolveMousePixelCoordinate(width, height);
            m_ExternalLut = m_ColorGradingSettings.externalLut;
            m_LogLut3DParams = ResolveExternalLutParams(m_ColorGradingSettings.externalLut, m_ColorGradingSettings.externalLutContribution);
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

            EnsureFallbackHistogramBuffer();

            var exposureSettings = m_ExposureData != null
                ? m_ExposureData.settings
                : AutoExposureSettingsData.CreateDefault();
            var currentExposureBuffer = ResolveCurrentExposureBuffer();
            var histogramBuffer = ResolveHistogramBuffer();
            var meterMask = exposureSettings.meterMask != null
                ? exposureSettings.meterMask
                : Texture2D.whiteTexture;

            m_Material.SetBuffer(AutoExposureHistogramBufferId, histogramBuffer);
            m_Material.SetBuffer(AutoExposureCurrentExposureBufferId, currentExposureBuffer);

            m_MaterialPropertyBlock ??= new MaterialPropertyBlock();
            var mpb = m_MaterialPropertyBlock;
            mpb.Clear();
            mpb.SetTexture(SourceTextureId, sourceTexture);
            mpb.SetTexture(AutoExposureMeterMaskId, meterMask);
            mpb.SetVector(SourceTextureScaleBiasId, m_SourceTextureScaleBias);
            mpb.SetVector(ExposureDebugStateId, m_ExposureDebugState);
            mpb.SetVector(ExposureDebugViewParamsId, m_ExposureDebugViewParams);
            mpb.SetVector(ExposureDebugRangeParamsId, m_ExposureDebugRangeParams);
            mpb.SetVector(ExposureDebugHistogramTransformId, m_ExposureDebugHistogramTransform);
            mpb.SetVector(ExposureDebugMeteringParamsId, m_ExposureDebugMeteringParams);
            mpb.SetVector(MousePixelCoordId, m_MousePixelCoord);
            mpb.SetInt(DebugTonemapModeId, (int)m_ColorGradingSettings.tonemappingMode);
            mpb.SetVector(LogLut3DParamsId, m_LogLut3DParams);
            mpb.SetVector(CustomToneCurveId, m_ColorGradingSettings.customToneCurve);
            mpb.SetVector(ToeSegmentAId, m_ColorGradingSettings.toeSegmentA);
            mpb.SetVector(ToeSegmentBId, m_ColorGradingSettings.toeSegmentB);
            mpb.SetVector(MidSegmentAId, m_ColorGradingSettings.midSegmentA);
            mpb.SetVector(MidSegmentBId, m_ColorGradingSettings.midSegmentB);
            mpb.SetVector(ShoSegmentAId, m_ColorGradingSettings.shoSegmentA);
            mpb.SetVector(ShoSegmentBId, m_ColorGradingSettings.shoSegmentB);

            if (m_ExternalLut != null)
                mpb.SetTexture(LogLut3DId, m_ExternalLut);

            nativeCmd.SetRenderTarget(m_OutputTexture);
            CoreUtils.DrawFullScreen(nativeCmd, m_Material, mpb, ResolvePassIndex(m_ResolvedSettings.mode));
        }

        public override void Dispose()
        {
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }

            m_ZeroHistogramBuffer?.Dispose();
            m_ZeroHistogramBuffer = null;
            m_MaterialPropertyBlock = null;
            m_ExternalLut = null;
            m_ShouldSkipExecution = false;
        }

        internal static ExposureDebugSettingsData ResolveSettings(VividRenderingDebugSettingsData data)
        {
            var debugExposure = 0f;
            var mode = ExposureDebugMode.None;
            var centerHistogramAroundMiddleGrey = false;
            var showTonemapCurveAlongHistogramView = true;
            var displayMaskOnly = false;
            var displayOnSceneOverlay = true;

            if (data == null)
            {
                return new ExposureDebugSettingsData(
                    debugExposure,
                    mode,
                    centerHistogramAroundMiddleGrey,
                    showTonemapCurveAlongHistogramView,
                    displayMaskOnly,
                    displayOnSceneOverlay);
            }

            mode = data.exposureMode;
            debugExposure = Mathf.Clamp(data.debugExposure, -16f, 16f);
            centerHistogramAroundMiddleGrey = data.centerHistogramAroundMiddleGrey;
            showTonemapCurveAlongHistogramView = data.showTonemapCurveAlongHistogramView;
            displayMaskOnly = data.displayMaskOnly;
            displayOnSceneOverlay = data.displayOnSceneOverlay;

            return new ExposureDebugSettingsData(
                debugExposure,
                mode,
                centerHistogramAroundMiddleGrey,
                showTonemapCurveAlongHistogramView,
                displayMaskOnly,
                displayOnSceneOverlay);
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

        private static RTHandle ResolveHandle(RenderGraphTexture texture)
        {
            return texture != null ? texture.innerHandle : null;
        }

        private void EnsureFallbackHistogramBuffer()
        {
            if (m_ZeroHistogramBuffer != null
                && m_ZeroHistogramBuffer.count == HistogramBucketCount
                && m_ZeroHistogramBuffer.stride == sizeof(uint))
            {
                return;
            }

            m_ZeroHistogramBuffer?.Dispose();
            m_ZeroHistogramBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                HistogramBucketCount,
                sizeof(uint));
            m_ZeroHistogramBuffer.name = "VividRP Exposure Debug Empty Histogram";
            m_ZeroHistogramBuffer.SetData(s_ZeroHistogramData);
        }

        private GraphicsBuffer ResolveCurrentExposureBuffer()
        {
            return m_ExposureData?.currentExposureBuffer
                   ?? m_ExposureData?.previousExposureBuffer
                   ?? m_ExposureData?.defaultExposureBuffer
                   ?? VividAutoExposureSystem.GetOrCreateDefaultExposureBuffer();
        }

        private GraphicsBuffer ResolveHistogramBuffer()
        {
            return m_ExposureData?.histogramBuffer ?? m_ZeroHistogramBuffer;
        }

        private static Vector4 ResolveAutoExposureDebugHistogramTransform(AutoExposureSettingsData settings)
        {
            var histogramScale = Mathf.Abs(settings.histogramScale) > 1e-6f
                ? settings.histogramScale
                : 1f;
            var middleGreyOffsetEV100 = -Mathf.Log(AutoExposureSettingsResolver.MiddleGrey, 2f);
            var histogramMinWhitePointEV100 = -settings.histogramBias / histogramScale;
            var histogramMaxWhitePointEV100 = (1f - settings.histogramBias) / histogramScale;
            if (histogramMaxWhitePointEV100 <= histogramMinWhitePointEV100)
                histogramMaxWhitePointEV100 = histogramMinWhitePointEV100 + 1f;

            var histogramMinAverageSceneEV100 = histogramMinWhitePointEV100 + middleGreyOffsetEV100;
            var histogramMaxAverageSceneEV100 = histogramMaxWhitePointEV100 + middleGreyOffsetEV100;
            var histogramRangeEV100 = Mathf.Max(
                histogramMaxAverageSceneEV100 - histogramMinAverageSceneEV100,
                1e-4f);

            return new Vector4(
                histogramMinAverageSceneEV100,
                histogramMaxAverageSceneEV100,
                1f / histogramRangeEV100,
                middleGreyOffsetEV100);
        }

        private static Vector4 ResolveExternalLutParams(Texture externalLut, float contribution)
        {
            if (externalLut == null)
                return Vector4.zero;

            var lutSize = Mathf.Max(2, ResolveTextureSize(externalLut));
            return new Vector4(1f / lutSize, lutSize - 1f, Mathf.Clamp01(contribution), 0f);
        }

        private static int ResolveTextureSize(Texture texture)
        {
            return texture switch
            {
                Texture3D texture3D => texture3D.width,
                RenderTexture renderTexture when renderTexture.dimension == TextureDimension.Tex3D => renderTexture.width,
                _ => 0
            };
        }

        private Vector4 ResolveMousePixelCoordinate(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return Vector4.zero;

            var defaultCoordinate = new Vector4(width * 0.5f, height * 0.5f, 0f, 0f);
            if (m_Camera == null || !Input.mousePresent)
                return defaultCoordinate;

            var mousePosition = Input.mousePosition;
            var pixelRect = m_Camera.pixelRect;
            if (pixelRect.width <= 0f || pixelRect.height <= 0f)
                return defaultCoordinate;

            var normalizedX = Mathf.Clamp01((mousePosition.x - pixelRect.x) / pixelRect.width);
            var normalizedY = Mathf.Clamp01((mousePosition.y - pixelRect.y) / pixelRect.height);

            return new Vector4(
                normalizedX * width,
                (1f - normalizedY) * height,
                0f,
                0f);
        }

        private static int ResolvePassIndex(ExposureDebugMode mode)
        {
            return mode switch
            {
                ExposureDebugMode.SceneEV100Values => 0,
                ExposureDebugMode.MeteringWeighted => 1,
                ExposureDebugMode.HistogramView => 2,
                _ => 3,
            };
        }
    }
}
