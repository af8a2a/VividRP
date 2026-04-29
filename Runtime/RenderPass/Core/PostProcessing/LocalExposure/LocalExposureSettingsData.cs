using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    internal struct LocalExposureSettingsData
    {
        private static readonly LocalExposureSettingsData s_Default = new()
        {
            enabled = false,
            highlightContrastScale = 1f,
            shadowContrastScale = 1f,
            detailStrength = 1f,
            blurredLuminanceBlend = 0.6f,
            blurredLuminanceKernelSizePercent = 50f,
            highlightThreshold = 0f,
            shadowThreshold = 0f,
            highlightThresholdStrength = 1f,
            shadowThresholdStrength = 1f,
            middleGreyExposureCompensation = 1f,
            highlightContrastCurveTexture = null,
            highlightContrastCurveMinEV100 = LocalExposureCurveUtility.DefaultCurveMinEV100,
            highlightContrastCurveInvRange = 1f / LocalExposureCurveUtility.DefaultCurveRange,
            highlightContrastCurveEnabled = false,
            shadowContrastCurveTexture = null,
            shadowContrastCurveMinEV100 = LocalExposureCurveUtility.DefaultCurveMinEV100,
            shadowContrastCurveInvRange = 1f / LocalExposureCurveUtility.DefaultCurveRange,
            shadowContrastCurveEnabled = false,
        };

        public bool enabled;
        public float highlightContrastScale;
        public float shadowContrastScale;
        public float detailStrength;
        public float blurredLuminanceBlend;
        public float blurredLuminanceKernelSizePercent;
        public float highlightThreshold;
        public float shadowThreshold;
        public float highlightThresholdStrength;
        public float shadowThresholdStrength;
        public float middleGreyExposureCompensation;
        public Texture highlightContrastCurveTexture;
        public float highlightContrastCurveMinEV100;
        public float highlightContrastCurveInvRange;
        public bool highlightContrastCurveEnabled;
        public Texture shadowContrastCurveTexture;
        public float shadowContrastCurveMinEV100;
        public float shadowContrastCurveInvRange;
        public bool shadowContrastCurveEnabled;

        public static LocalExposureSettingsData CreateDefault()
        {
            return s_Default;
        }
    }

    internal static class LocalExposureSettingsResolver
    {
        internal static LocalExposureSettingsData Resolve()
        {
            var settings = LocalExposureSettingsData.CreateDefault();
            var stack = VolumeManager.instance.stack;
            if (stack == null)
                return settings;

            var localExposure = stack.GetComponent<LocalExposure>();
            if (localExposure == null || !localExposure.IsActive())
                return settings;

            settings.enabled = true;
            settings.highlightContrastScale = Mathf.Clamp01(localExposure.highlightContrastScale?.value ?? 1f);
            settings.shadowContrastScale = Mathf.Clamp01(localExposure.shadowContrastScale?.value ?? 1f);
            settings.detailStrength = Mathf.Clamp(localExposure.detailStrength?.value ?? 1f, 0f, 4f);
            settings.blurredLuminanceBlend = Mathf.Clamp01(localExposure.blurredLuminanceBlend?.value ?? 0.6f);
            settings.blurredLuminanceKernelSizePercent = Mathf.Clamp(
                localExposure.blurredLuminanceKernelSizePercent?.value ?? 50f,
                0f,
                100f);
            settings.highlightThreshold = Mathf.Clamp(localExposure.highlightThreshold?.value ?? 0f, 0f, 4f);
            settings.shadowThreshold = Mathf.Clamp(localExposure.shadowThreshold?.value ?? 0f, 0f, 4f);
            settings.highlightThresholdStrength = Mathf.Clamp01(localExposure.highlightThresholdStrength?.value ?? 1f);
            settings.shadowThresholdStrength = Mathf.Clamp01(localExposure.shadowThresholdStrength?.value ?? 1f);
            settings.middleGreyExposureCompensation = Mathf.Pow(
                2f,
                Mathf.Clamp(localExposure.middleGreyBias?.value ?? 0f, -15f, 15f));

            var highlightCurve = LocalExposureCurveUtility.Resolve(
                localExposure.highlightContrastCurve?.value,
                "VividRP Local Exposure Highlight Contrast Curve");
            settings.highlightContrastCurveTexture = highlightCurve.texture;
            settings.highlightContrastCurveMinEV100 = highlightCurve.minEV100;
            settings.highlightContrastCurveInvRange = highlightCurve.invRange;
            settings.highlightContrastCurveEnabled = highlightCurve.enabled;

            var shadowCurve = LocalExposureCurveUtility.Resolve(
                localExposure.shadowContrastCurve?.value,
                "VividRP Local Exposure Shadow Contrast Curve");
            settings.shadowContrastCurveTexture = shadowCurve.texture;
            settings.shadowContrastCurveMinEV100 = shadowCurve.minEV100;
            settings.shadowContrastCurveInvRange = shadowCurve.invRange;
            settings.shadowContrastCurveEnabled = shadowCurve.enabled;
            return settings;
        }
    }

    internal readonly struct LocalExposureCurveTextureData
    {
        public readonly Texture texture;
        public readonly float minEV100;
        public readonly float invRange;
        public readonly bool enabled;

        public LocalExposureCurveTextureData(Texture texture, float minEV100, float invRange, bool enabled)
        {
            this.texture = texture;
            this.minEV100 = minEV100;
            this.invRange = invRange;
            this.enabled = enabled;
        }
    }

    internal static class LocalExposureCurveUtility
    {
        private const int CurveSampleCount = 256;

        internal const float DefaultCurveMinEV100 = -16f;
        internal const float DefaultCurveMaxEV100 = 16f;
        internal const float DefaultCurveRange = DefaultCurveMaxEV100 - DefaultCurveMinEV100;

        private static readonly Color[] s_CurveSamples = new Color[CurveSampleCount];
        private static readonly LocalExposureCurveTextureCache s_HighlightCurveCache = new();
        private static readonly LocalExposureCurveTextureCache s_ShadowCurveCache = new();

        internal static LocalExposureCurveTextureData Resolve(AnimationCurve curve, string textureName)
        {
            if (!HasCurve(curve))
            {
                return new LocalExposureCurveTextureData(
                    Texture2D.blackTexture,
                    DefaultCurveMinEV100,
                    1f / DefaultCurveRange,
                    false);
            }

            var cache = textureName != null && textureName.Contains("Shadow")
                ? s_ShadowCurveCache
                : s_HighlightCurveCache;
            return cache.Resolve(curve, textureName);
        }

        internal static void Dispose()
        {
            s_HighlightCurveCache.Dispose();
            s_ShadowCurveCache.Dispose();
        }

        internal static bool HasCurve(AnimationCurve curve)
        {
            return curve != null && curve.length > 0;
        }

        internal static Vector2 ResolveCurveDomain(AnimationCurve curve)
        {
            if (!HasCurve(curve))
                return new Vector2(DefaultCurveMinEV100, DefaultCurveMaxEV100);

            var minEV100 = curve[0].time;
            var maxEV100 = curve[curve.length - 1].time;
            if (maxEV100 < minEV100)
                (minEV100, maxEV100) = (maxEV100, minEV100);

            if (Mathf.Abs(maxEV100 - minEV100) < 1e-4f)
            {
                minEV100 -= 1f;
                maxEV100 += 1f;
            }

            return new Vector2(minEV100, maxEV100);
        }

        private static void RebuildCurveTexture(Texture2D texture, AnimationCurve curve, Vector2 curveDomain)
        {
            for (var sampleIndex = 0; sampleIndex < CurveSampleCount; sampleIndex++)
            {
                var sampleT = sampleIndex / (float)(CurveSampleCount - 1);
                var ev100 = Mathf.Lerp(curveDomain.x, curveDomain.y, sampleT);
                s_CurveSamples[sampleIndex] = new Color(curve.Evaluate(ev100), 0f, 0f, 0f);
            }

            texture.SetPixels(s_CurveSamples);
            texture.Apply(false, false);
        }

        private static int ComputeCurveHash(AnimationCurve curve, Vector2 curveDomain)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + curveDomain.x.GetHashCode();
                hash = hash * 31 + curveDomain.y.GetHashCode();
                hash = hash * 31 + curve.preWrapMode.GetHashCode();
                hash = hash * 31 + curve.postWrapMode.GetHashCode();
                hash = hash * 31 + curve.length;

                for (var keyIndex = 0; keyIndex < curve.length; keyIndex++)
                {
                    var key = curve[keyIndex];
                    hash = hash * 31 + key.time.GetHashCode();
                    hash = hash * 31 + key.value.GetHashCode();
                    hash = hash * 31 + key.inTangent.GetHashCode();
                    hash = hash * 31 + key.outTangent.GetHashCode();
                    hash = hash * 31 + key.inWeight.GetHashCode();
                    hash = hash * 31 + key.outWeight.GetHashCode();
                    hash = hash * 31 + key.weightedMode.GetHashCode();
                }

                return hash;
            }
        }

        private sealed class LocalExposureCurveTextureCache
        {
            private Texture2D m_Texture;
            private int m_CachedCurveHash;
            private bool m_HasCachedCurve;
            private Vector2 m_CachedCurveDomain = new(DefaultCurveMinEV100, DefaultCurveMaxEV100);

            public LocalExposureCurveTextureData Resolve(AnimationCurve curve, string textureName)
            {
                EnsureTexture(textureName);

                var curveDomain = ResolveCurveDomain(curve);
                var curveHash = ComputeCurveHash(curve, curveDomain);
                if (!m_HasCachedCurve || curveHash != m_CachedCurveHash)
                {
                    RebuildCurveTexture(m_Texture, curve, curveDomain);
                    m_CachedCurveHash = curveHash;
                    m_CachedCurveDomain = curveDomain;
                    m_HasCachedCurve = true;
                }

                return new LocalExposureCurveTextureData(
                    m_Texture,
                    m_CachedCurveDomain.x,
                    1f / Mathf.Max(m_CachedCurveDomain.y - m_CachedCurveDomain.x, 1e-4f),
                    true);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(m_Texture);
                m_Texture = null;
                m_CachedCurveHash = 0;
                m_HasCachedCurve = false;
                m_CachedCurveDomain = new Vector2(DefaultCurveMinEV100, DefaultCurveMaxEV100);
            }

            private void EnsureTexture(string textureName)
            {
                if (m_Texture != null)
                    return;

                m_Texture = new Texture2D(CurveSampleCount, 1, TextureFormat.RGBAFloat, false, true)
                {
                    name = string.IsNullOrEmpty(textureName) ? "VividRP Local Exposure Curve" : textureName,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }
    }
}
