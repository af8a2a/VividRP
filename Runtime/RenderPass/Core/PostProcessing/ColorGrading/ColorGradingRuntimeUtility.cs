using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal enum ColorGradingTonemappingShaderMode
    {
        None,
        Neutral,
        AcesApprox,
        AcesFull,
        GranTurismo,
        AgX,
        KhronosPBR,
        Custom,
        External
    }

    internal static class ColorGradingSpaceUtility
    {
        internal const string GradeInSrgbKeyword = "GRADE_IN_SRGB";
        internal const string GradeInAcesCgKeyword = "GRADE_IN_ACESCG";

        internal static ColorGradingSpace ResolveColorGradingSpace(VividRenderPipelineAsset pipelineAsset)
        {
            return pipelineAsset?.ColorGradingSpace ?? ColorGradingSpace.sRGB;
        }

        internal static string GetColorGradingSpaceKeyword(ColorGradingSpace colorGradingSpace)
        {
            return colorGradingSpace switch
            {
                ColorGradingSpace.sRGB => GradeInSrgbKeyword,
                ColorGradingSpace.AcesCg => GradeInAcesCgKeyword,
                _ => GradeInSrgbKeyword
            };
        }
    }

    internal struct ColorGradingSettingsData
    {
        private static readonly ColorGradingSettingsData s_Default = CreateDefaultData();

        public bool enableColorGrading;
        public float postExposureLinear;
        public ColorGradingTonemappingShaderMode tonemappingMode;
        public Texture externalLut;
        public float externalLutContribution;
        public Vector4 colorBalance;
        public Vector4 colorFilter;
        public Vector4 channelMixerRed;
        public Vector4 channelMixerGreen;
        public Vector4 channelMixerBlue;
        public Vector4 hueSatCon;
        public Vector4 lift;
        public Vector4 gamma;
        public Vector4 gain;
        public Vector4 shadows;
        public Vector4 midtones;
        public Vector4 highlights;
        public Vector4 shaHiLimits;
        public Vector4 splitShadows;
        public Vector4 splitHighlights;
        public Vector4 granTurismoParams0;
        public Vector4 granTurismoParams1;
        public Vector4 customToneCurve;
        public Vector4 toeSegmentA;
        public Vector4 toeSegmentB;
        public Vector4 midSegmentA;
        public Vector4 midSegmentB;
        public Vector4 shoSegmentA;
        public Vector4 shoSegmentB;

        public bool RequiresLut => enableColorGrading || tonemappingMode != ColorGradingTonemappingShaderMode.None;

        public bool RequiresPostProcessing => RequiresLut || !Mathf.Approximately(postExposureLinear, 1f);

        public static ColorGradingSettingsData CreateDefault()
        {
            return s_Default;
        }

        private static ColorGradingSettingsData CreateDefaultData()
        {
            var splitToning = ColorUtils.PrepareSplitToning(
                new Vector4(0.5f, 0.5f, 0.5f, 1f),
                new Vector4(0.5f, 0.5f, 0.5f, 1f),
                0f);
            var liftGammaGain = ColorUtils.PrepareLiftGammaGain(
                new Vector4(1f, 1f, 1f, 0f),
                new Vector4(1f, 1f, 1f, 0f),
                new Vector4(1f, 1f, 1f, 0f));
            var shadowsMidtonesHighlights = ColorUtils.PrepareShadowsMidtonesHighlights(
                new Vector4(1f, 1f, 1f, 0f),
                new Vector4(1f, 1f, 1f, 0f),
                new Vector4(1f, 1f, 1f, 0f));

            return new ColorGradingSettingsData
            {
                enableColorGrading = false,
                postExposureLinear = 1f,
                tonemappingMode = ColorGradingTonemappingShaderMode.None,
                externalLut = null,
                externalLutContribution = 0f,
                colorBalance = new Vector4(1f, 1f, 1f, 0f),
                colorFilter = new Vector4(1f, 1f, 1f, 0f),
                channelMixerRed = new Vector4(1f, 0f, 0f, 0f),
                channelMixerGreen = new Vector4(0f, 1f, 0f, 0f),
                channelMixerBlue = new Vector4(0f, 0f, 1f, 0f),
                hueSatCon = new Vector4(0f, 1f, 1f, 0f),
                lift = liftGammaGain.Item1,
                gamma = liftGammaGain.Item2,
                gain = liftGammaGain.Item3,
                shadows = shadowsMidtonesHighlights.Item1,
                midtones = shadowsMidtonesHighlights.Item2,
                highlights = shadowsMidtonesHighlights.Item3,
                shaHiLimits = new Vector4(0f, 0.3f, 0.55f, 1f),
                splitShadows = splitToning.Item1,
                splitHighlights = splitToning.Item2,
                granTurismoParams0 = Vector4.zero,
                granTurismoParams1 = Vector4.zero,
                customToneCurve = Vector4.zero,
                toeSegmentA = Vector4.zero,
                toeSegmentB = Vector4.zero,
                midSegmentA = Vector4.zero,
                midSegmentB = Vector4.zero,
                shoSegmentA = Vector4.zero,
                shoSegmentB = Vector4.zero,
            };
        }
    }

    internal sealed class VividColorGradingData : ContextItem
    {
        internal bool isResolved;
        internal ColorGradingSettingsData settings;
        internal ColorCurves curves;

        public override void Reset()
        {
            isResolved = false;
            settings = ColorGradingSettingsData.CreateDefault();
            curves = null;
        }
    }

    internal static class ColorGradingSettingsResolver
    {
        private static readonly HableCurve s_CustomToneCurve = new();

        internal static ColorGradingSettingsData Resolve()
        {
            return ResolveFromStack(VolumeManager.instance.stack, out _);
        }

        internal static ColorGradingSettingsData Resolve(ContextContainer frameData, out ColorCurves curves)
        {
            if (frameData == null)
                return ResolveFromStack(VolumeManager.instance.stack, out curves);

            var data = frameData.GetOrCreate<VividColorGradingData>();
            if (!data.isResolved)
            {
                data.settings = ResolveFromStack(VolumeManager.instance.stack, out data.curves);
                data.isResolved = true;
            }

            curves = data.curves;
            return data.settings;
        }

        internal static bool TryGetResolved(
            ContextContainer frameData,
            out ColorGradingSettingsData settings,
            out ColorCurves curves)
        {
            if (frameData != null && frameData.Contains<VividColorGradingData>())
            {
                var data = frameData.Get<VividColorGradingData>();
                if (data.isResolved)
                {
                    settings = data.settings;
                    curves = data.curves;
                    return true;
                }
            }

            settings = ColorGradingSettingsData.CreateDefault();
            curves = null;
            return false;
        }

        private static ColorGradingSettingsData ResolveFromStack(VolumeStack stack, out ColorCurves curves)
        {
            var settings = ColorGradingSettingsData.CreateDefault();
            curves = null;
            if (stack == null)
                return settings;

            var whiteBalance = stack.GetComponent<WhiteBalance>();
            var colorAdjustments = stack.GetComponent<ColorAdjustments>();
            var channelMixer = stack.GetComponent<ChannelMixer>();
            var splitToning = stack.GetComponent<SplitToning>();
            var liftGammaGain = stack.GetComponent<LiftGammaGain>();
            var shadowsMidtonesHighlights = stack.GetComponent<ShadowsMidtonesHighlights>();
            curves = stack.GetComponent<ColorCurves>();
            var tonemapping = stack.GetComponent<Tonemapping>();

            settings.postExposureLinear = ResolvePostExposure(colorAdjustments);

            if (whiteBalance != null)
            {
                var lms = ColorUtils.ColorBalanceToLMSCoeffs(whiteBalance.temperature.value, whiteBalance.tint.value);
                settings.colorBalance = new Vector4(lms.x, lms.y, lms.z, 0f);
                settings.enableColorGrading |= whiteBalance.IsActive();
            }

            if (colorAdjustments != null)
            {
                settings.colorFilter = ToVector4(colorAdjustments.colorFilter.value.linear);
                settings.hueSatCon = BuildHueSatCon(
                    colorAdjustments.hueShift.value,
                    colorAdjustments.saturation.value,
                    colorAdjustments.contrast.value);
                settings.enableColorGrading |= IsColorAdjustmentsLutActive(colorAdjustments);
            }

            if (channelMixer != null)
            {
                settings.channelMixerRed = BuildChannelMixerVector(
                    channelMixer.redOutRedIn.value,
                    channelMixer.redOutGreenIn.value,
                    channelMixer.redOutBlueIn.value);
                settings.channelMixerGreen = BuildChannelMixerVector(
                    channelMixer.greenOutRedIn.value,
                    channelMixer.greenOutGreenIn.value,
                    channelMixer.greenOutBlueIn.value);
                settings.channelMixerBlue = BuildChannelMixerVector(
                    channelMixer.blueOutRedIn.value,
                    channelMixer.blueOutGreenIn.value,
                    channelMixer.blueOutBlueIn.value);
                settings.enableColorGrading |= channelMixer.IsActive();
            }

            if (splitToning != null)
            {
                var preparedSplitToning = ColorUtils.PrepareSplitToning(
                    ToVector4(splitToning.shadows.value),
                    ToVector4(splitToning.highlights.value),
                    splitToning.balance.value);
                settings.splitShadows = preparedSplitToning.Item1;
                settings.splitHighlights = preparedSplitToning.Item2;
                settings.enableColorGrading |= splitToning.IsActive();
            }

            if (liftGammaGain != null)
            {
                var preparedLiftGammaGain = ColorUtils.PrepareLiftGammaGain(
                    ToVector4(liftGammaGain.lift.value),
                    ToVector4(liftGammaGain.gamma.value),
                    ToVector4(liftGammaGain.gain.value));
                settings.lift = preparedLiftGammaGain.Item1;
                settings.gamma = preparedLiftGammaGain.Item2;
                settings.gain = preparedLiftGammaGain.Item3;
                settings.enableColorGrading |= liftGammaGain.IsActive();
            }

            if (shadowsMidtonesHighlights != null)
            {
                var preparedShadowsMidtonesHighlights = ColorUtils.PrepareShadowsMidtonesHighlights(
                    ToVector4(shadowsMidtonesHighlights.shadows.value),
                    ToVector4(shadowsMidtonesHighlights.midtones.value),
                    ToVector4(shadowsMidtonesHighlights.highlights.value));
                settings.shadows = preparedShadowsMidtonesHighlights.Item1;
                settings.midtones = preparedShadowsMidtonesHighlights.Item2;
                settings.highlights = preparedShadowsMidtonesHighlights.Item3;
                settings.shaHiLimits = BuildShaHiLimits(
                    shadowsMidtonesHighlights.shadowsStart.value,
                    shadowsMidtonesHighlights.shadowsEnd.value,
                    shadowsMidtonesHighlights.highlightsStart.value,
                    shadowsMidtonesHighlights.highlightsEnd.value);
                settings.enableColorGrading |= shadowsMidtonesHighlights.IsActive();
            }

            if (curves != null)
            {
                settings.enableColorGrading |= curves.IsActive();
            }

            ResolveTonemapping(ref settings, tonemapping);
            return settings;
        }

        internal static float ResolvePostExposure(ColorAdjustments colorAdjustments)
        {
            return colorAdjustments == null
                ? 1f
                : Mathf.Pow(2f, colorAdjustments.postExposure.value);
        }

        internal static Vector4 BuildHueSatCon(float hueShift, float saturation, float contrast)
        {
            return new Vector4(
                hueShift / 360f,
                saturation * 0.01f + 1f,
                contrast * 0.01f + 1f,
                0f);
        }

        internal static Vector4 BuildChannelMixerVector(float red, float green, float blue)
        {
            return new Vector4(red * 0.01f, green * 0.01f, blue * 0.01f, 0f);
        }

        internal static Vector4 BuildShaHiLimits(float shadowsStart, float shadowsEnd, float highlightsStart, float highlightsEnd)
        {
            return new Vector4(shadowsStart, shadowsEnd, highlightsStart, highlightsEnd);
        }

        internal static Vector4 BuildGranTurismoParams0(float maxBrightness, float contrast, float linearSectionStart, float linearSectionLength)
        {
            return new Vector4(maxBrightness, contrast, linearSectionStart, linearSectionLength);
        }

        internal static Vector4 BuildGranTurismoParams1(float blackPow, float blackMin)
        {
            return new Vector4(blackPow, blackMin, 0f, 0f);
        }

        internal static bool IsColorAdjustmentsLutActive(ColorAdjustments colorAdjustments)
        {
            if (colorAdjustments == null)
                return false;

            return !Mathf.Approximately(colorAdjustments.contrast.value, 0f)
                || !ColorGradingCurvePresets.IsApproximately(colorAdjustments.colorFilter.value, Color.white)
                || !Mathf.Approximately(colorAdjustments.hueShift.value, 0f)
                || !Mathf.Approximately(colorAdjustments.saturation.value, 0f);
        }

        private static void ResolveTonemapping(ref ColorGradingSettingsData settings, Tonemapping tonemapping)
        {
            if (tonemapping == null)
            {
                settings.tonemappingMode = ColorGradingTonemappingShaderMode.None;
                return;
            }

            switch (tonemapping.mode.value)
            {
                case TonemappingMode.None:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.None;
                    break;
                case TonemappingMode.Neutral:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.Neutral;
                    break;
                case TonemappingMode.ACES:
                    settings.tonemappingMode = tonemapping.useFullACES.value
                        ? ColorGradingTonemappingShaderMode.AcesFull
                        : ColorGradingTonemappingShaderMode.AcesApprox;
                    break;
                case TonemappingMode.GranTurismo:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.GranTurismo;
                    settings.granTurismoParams0 = BuildGranTurismoParams0(
                        tonemapping.maxBrightness.value,
                        tonemapping.contrast.value,
                        tonemapping.linearSectionStart.value,
                        tonemapping.linearSectionLength.value);
                    settings.granTurismoParams1 = BuildGranTurismoParams1(
                        tonemapping.blackPow.value,
                        tonemapping.blackMin.value);
                    break;
                case TonemappingMode.AgX:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.AgX;
                    break;
                case TonemappingMode.KhronosPBR:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.KhronosPBR;
                    break;
                case TonemappingMode.Custom:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.Custom;
                    s_CustomToneCurve.Init(
                        tonemapping.toeStrength.value,
                        tonemapping.toeLength.value,
                        tonemapping.shoulderStrength.value,
                        tonemapping.shoulderLength.value,
                        tonemapping.shoulderAngle.value,
                        tonemapping.gamma.value);
                    settings.customToneCurve = s_CustomToneCurve.uniforms.curve;
                    settings.toeSegmentA = s_CustomToneCurve.uniforms.toeSegmentA;
                    settings.toeSegmentB = s_CustomToneCurve.uniforms.toeSegmentB;
                    settings.midSegmentA = s_CustomToneCurve.uniforms.midSegmentA;
                    settings.midSegmentB = s_CustomToneCurve.uniforms.midSegmentB;
                    settings.shoSegmentA = s_CustomToneCurve.uniforms.shoSegmentA;
                    settings.shoSegmentB = s_CustomToneCurve.uniforms.shoSegmentB;
                    break;
                case TonemappingMode.External:
                    settings.externalLut = tonemapping.lutTexture.value;
                    settings.externalLutContribution = tonemapping.lutContribution.value;
                    settings.tonemappingMode = settings.externalLut != null
                        ? ColorGradingTonemappingShaderMode.External
                        : ColorGradingTonemappingShaderMode.None;
                    break;
                default:
                    settings.tonemappingMode = ColorGradingTonemappingShaderMode.None;
                    break;
            }
        }

        private static Vector4 ToVector4(Color color)
        {
            return new Vector4(color.r, color.g, color.b, color.a);
        }
    }

    internal sealed class ColorGradingLutBuilder : IDisposable
    {
        internal const int LutSize = 32;
        private const int ThreadGroupSize = 4;

        private static readonly int OutputTextureId = Shader.PropertyToID("_OutputTexture");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int LogLut3DId = Shader.PropertyToID("_LogLut3D");
        private static readonly int LogLut3DParamsId = Shader.PropertyToID("_LogLut3D_Params");
        private static readonly int ColorBalanceId = Shader.PropertyToID("_ColorBalance");
        private static readonly int ColorFilterId = Shader.PropertyToID("_ColorFilter");
        private static readonly int ChannelMixerRedId = Shader.PropertyToID("_ChannelMixerRed");
        private static readonly int ChannelMixerGreenId = Shader.PropertyToID("_ChannelMixerGreen");
        private static readonly int ChannelMixerBlueId = Shader.PropertyToID("_ChannelMixerBlue");
        private static readonly int HueSatConId = Shader.PropertyToID("_HueSatCon");
        private static readonly int LiftId = Shader.PropertyToID("_Lift");
        private static readonly int GammaId = Shader.PropertyToID("_Gamma");
        private static readonly int GainId = Shader.PropertyToID("_Gain");
        private static readonly int ShadowsId = Shader.PropertyToID("_Shadows");
        private static readonly int MidtonesId = Shader.PropertyToID("_Midtones");
        private static readonly int HighlightsId = Shader.PropertyToID("_Highlights");
        private static readonly int ShaHiLimitsId = Shader.PropertyToID("_ShaHiLimits");
        private static readonly int SplitShadowsId = Shader.PropertyToID("_SplitShadows");
        private static readonly int SplitHighlightsId = Shader.PropertyToID("_SplitHighlights");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly int GtToneMapParams0Id = Shader.PropertyToID("_GTToneMap_Params0");
        private static readonly int GtToneMapParams1Id = Shader.PropertyToID("_GTToneMap_Params1");
        private static readonly int CustomToneCurveId = Shader.PropertyToID("_CustomToneCurve");
        private static readonly int ToeSegmentAId = Shader.PropertyToID("_ToeSegmentA");
        private static readonly int ToeSegmentBId = Shader.PropertyToID("_ToeSegmentB");
        private static readonly int MidSegmentAId = Shader.PropertyToID("_MidSegmentA");
        private static readonly int MidSegmentBId = Shader.PropertyToID("_MidSegmentB");
        private static readonly int ShoSegmentAId = Shader.PropertyToID("_ShoSegmentA");
        private static readonly int ShoSegmentBId = Shader.PropertyToID("_ShoSegmentB");
        private static readonly int CurveMasterId = Shader.PropertyToID("_CurveMaster");
        private static readonly int CurveRedId = Shader.PropertyToID("_CurveRed");
        private static readonly int CurveGreenId = Shader.PropertyToID("_CurveGreen");
        private static readonly int CurveBlueId = Shader.PropertyToID("_CurveBlue");
        private static readonly int CurveHueVsHueId = Shader.PropertyToID("_CurveHueVsHue");
        private static readonly int CurveHueVsSatId = Shader.PropertyToID("_CurveHueVsSat");
        private static readonly int CurveSatVsSatId = Shader.PropertyToID("_CurveSatVsSat");
        private static readonly int CurveLumVsSatId = Shader.PropertyToID("_CurveLumVsSat");

        private readonly ComputeShader m_Shader;
        private readonly int m_Kernel;
        private readonly LocalKeyword m_TonemappingNoneKeyword;
        private readonly LocalKeyword m_TonemappingNeutralKeyword;
        private readonly LocalKeyword m_TonemappingAcesApproxKeyword;
        private readonly LocalKeyword m_TonemappingAcesFullKeyword;
        private readonly LocalKeyword m_TonemappingGranTurismoKeyword;
        private readonly LocalKeyword m_TonemappingAgXKeyword;
        private readonly LocalKeyword m_TonemappingKhronosPbrKeyword;
        private readonly LocalKeyword m_TonemappingCustomKeyword;
        private readonly LocalKeyword m_TonemappingExternalKeyword;
        private readonly LocalKeyword m_GradeInSrgbKeyword;
        private readonly LocalKeyword m_GradeInAcesCgKeyword;
        private readonly LocalKeyword m_HdrColorspaceConversionKeyword;

        internal ColorGradingLutBuilder()
        {
            var resources = PipelineResourceManager.Get<PostProcessingShader>();
            m_Shader = resources != null ? resources.colorGradingShader : null;
            m_Kernel = m_Shader != null ? m_Shader.FindKernel("KBuild") : -1;
            m_TonemappingNoneKeyword = CreateKeyword("TONEMAPPING_NONE");
            m_TonemappingNeutralKeyword = CreateKeyword("TONEMAPPING_NEUTRAL");
            m_TonemappingAcesApproxKeyword = CreateKeyword("TONEMAPPING_ACES_APPROX");
            m_TonemappingAcesFullKeyword = CreateKeyword("TONEMAPPING_ACES_FULL");
            m_TonemappingGranTurismoKeyword = CreateKeyword("TONEMAPPING_GRAN_TURISMO");
            m_TonemappingAgXKeyword = CreateKeyword("TONEMAPPING_AGX");
            m_TonemappingKhronosPbrKeyword = CreateKeyword("TONEMAPPING_KHRONOS_PBR");
            m_TonemappingCustomKeyword = CreateKeyword("TONEMAPPING_CUSTOM");
            m_TonemappingExternalKeyword = CreateKeyword("TONEMAPPING_EXTERNAL");
            m_GradeInSrgbKeyword = CreateKeyword("GRADE_IN_SRGB");
            m_GradeInAcesCgKeyword = CreateKeyword("GRADE_IN_ACESCG");
            m_HdrColorspaceConversionKeyword = CreateKeyword("HDR_COLORSPACE_CONVERSION");
        }

        internal bool IsValid => m_Shader != null && m_Kernel >= 0;

        public void Dispose()
        {
        }

        private LocalKeyword CreateKeyword(string keywordName)
        {
            return m_Shader != null
                ? new LocalKeyword(m_Shader, keywordName)
                : default;
        }

        internal void Build(CommandBuffer cmd, in ColorGradingSettingsData settings, ColorCurves curves, Texture externalLut, TextureHandle output)
        {
            if (cmd == null || !IsValid || !output.IsValid())
                return;

            var colorGradingSpace = ColorGradingSpaceUtility.ResolveColorGradingSpace(VividRenderPipelineAsset.GetActiveAsset());
            SetKeywords(cmd, settings.tonemappingMode, colorGradingSpace);
            cmd.SetComputeVectorParam(m_Shader, SizeId, new Vector4(LutSize, 1f / (LutSize - 1f), 0f, 0f));
            cmd.SetComputeVectorParam(m_Shader, LogLut3DParamsId, new Vector4(1f / LutSize, LutSize - 1f, settings.externalLutContribution, 0f));
            cmd.SetComputeVectorParam(m_Shader, ColorBalanceId, settings.colorBalance);
            cmd.SetComputeVectorParam(m_Shader, ColorFilterId, settings.colorFilter);
            cmd.SetComputeVectorParam(m_Shader, ChannelMixerRedId, settings.channelMixerRed);
            cmd.SetComputeVectorParam(m_Shader, ChannelMixerGreenId, settings.channelMixerGreen);
            cmd.SetComputeVectorParam(m_Shader, ChannelMixerBlueId, settings.channelMixerBlue);
            cmd.SetComputeVectorParam(m_Shader, HueSatConId, settings.hueSatCon);
            cmd.SetComputeVectorParam(m_Shader, LiftId, settings.lift);
            cmd.SetComputeVectorParam(m_Shader, GammaId, settings.gamma);
            cmd.SetComputeVectorParam(m_Shader, GainId, settings.gain);
            cmd.SetComputeVectorParam(m_Shader, ShadowsId, settings.shadows);
            cmd.SetComputeVectorParam(m_Shader, MidtonesId, settings.midtones);
            cmd.SetComputeVectorParam(m_Shader, HighlightsId, settings.highlights);
            cmd.SetComputeVectorParam(m_Shader, ShaHiLimitsId, settings.shaHiLimits);
            cmd.SetComputeVectorParam(m_Shader, SplitShadowsId, settings.splitShadows);
            cmd.SetComputeVectorParam(m_Shader, SplitHighlightsId, settings.splitHighlights);
            cmd.SetComputeVectorParam(m_Shader, ParamsId, new Vector4(settings.enableColorGrading ? 1f : 0f, 0f, 0f, 0f));
            cmd.SetComputeVectorParam(m_Shader, GtToneMapParams0Id, settings.granTurismoParams0);
            cmd.SetComputeVectorParam(m_Shader, GtToneMapParams1Id, settings.granTurismoParams1);
            cmd.SetComputeVectorParam(m_Shader, CustomToneCurveId, settings.customToneCurve);
            cmd.SetComputeVectorParam(m_Shader, ToeSegmentAId, settings.toeSegmentA);
            cmd.SetComputeVectorParam(m_Shader, ToeSegmentBId, settings.toeSegmentB);
            cmd.SetComputeVectorParam(m_Shader, MidSegmentAId, settings.midSegmentA);
            cmd.SetComputeVectorParam(m_Shader, MidSegmentBId, settings.midSegmentB);
            cmd.SetComputeVectorParam(m_Shader, ShoSegmentAId, settings.shoSegmentA);
            cmd.SetComputeVectorParam(m_Shader, ShoSegmentBId, settings.shoSegmentB);

            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveMasterId, curves.master.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveRedId, curves.red.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveGreenId, curves.green.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveBlueId, curves.blue.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveHueVsHueId, curves.hueVsHue.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveHueVsSatId, curves.hueVsSat.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveSatVsSatId, curves.satVsSat.value.GetTexture());
            cmd.SetComputeTextureParam(m_Shader, m_Kernel, CurveLumVsSatId, curves.lumVsSat.value.GetTexture());

            if (settings.tonemappingMode == ColorGradingTonemappingShaderMode.External && externalLut != null)
                cmd.SetComputeTextureParam(m_Shader, m_Kernel, LogLut3DId, externalLut);

            cmd.SetComputeTextureParam(m_Shader, m_Kernel, OutputTextureId, output);

            var dispatchCount = Mathf.CeilToInt(LutSize / (float)ThreadGroupSize);
            cmd.DispatchCompute(m_Shader, m_Kernel, dispatchCount, dispatchCount, dispatchCount);
        }

        private void SetKeywords(
            CommandBuffer cmd,
            ColorGradingTonemappingShaderMode tonemappingMode,
            ColorGradingSpace colorGradingSpace)
        {
            SetKeyword(cmd, m_TonemappingNoneKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.None);
            SetKeyword(cmd, m_TonemappingNeutralKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.Neutral);
            SetKeyword(cmd, m_TonemappingAcesApproxKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.AcesApprox);
            SetKeyword(cmd, m_TonemappingAcesFullKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.AcesFull);
            SetKeyword(cmd, m_TonemappingGranTurismoKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.GranTurismo);
            SetKeyword(cmd, m_TonemappingAgXKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.AgX);
            SetKeyword(cmd, m_TonemappingKhronosPbrKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.KhronosPBR);
            SetKeyword(cmd, m_TonemappingCustomKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.Custom);
            SetKeyword(cmd, m_TonemappingExternalKeyword, tonemappingMode == ColorGradingTonemappingShaderMode.External);
            var colorGradingSpaceKeyword = ColorGradingSpaceUtility.GetColorGradingSpaceKeyword(colorGradingSpace);
            SetKeyword(cmd, m_GradeInSrgbKeyword, colorGradingSpaceKeyword == ColorGradingSpaceUtility.GradeInSrgbKeyword);
            SetKeyword(cmd, m_GradeInAcesCgKeyword, colorGradingSpaceKeyword == ColorGradingSpaceUtility.GradeInAcesCgKeyword);
            SetKeyword(cmd, m_HdrColorspaceConversionKeyword, false);
        }

        private void SetKeyword(CommandBuffer cmd, LocalKeyword keyword, bool value)
        {
            if (!keyword.isValid)
                return;

            cmd.SetKeyword(m_Shader, keyword, value);
        }
    }
}
