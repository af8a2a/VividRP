using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(ScreenSpaceLensFlare))]
    internal sealed class ScreenSpaceLensFlareEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_Intensity = EditorGUIUtility.TrTextContent(
            "Intensity",
            "Sets the global intensity of the Screen Space Lens Flare effect. When set to 0, the pass is skipped.");
        private static readonly GUIContent s_TintColor = EditorGUIUtility.TrTextContent(
            "Tint Color",
            "Sets the color used to tint all flares.");
        private static readonly GUIContent s_BloomMip = EditorGUIUtility.TrTextContent(
            "Bloom Mip Bias",
            "Controls the Bloom mip used as a source for the Lens Flare effect.");
        private static readonly GUIContent s_FirstFlareIntensity = EditorGUIUtility.TrTextContent(
            "Regular Multiplier",
            "Controls the intensity of the regular flare sample.");
        private static readonly GUIContent s_SecondaryFlareIntensity = EditorGUIUtility.TrTextContent(
            "Reversed Multiplier",
            "Controls the intensity of the reversed flare sample.");
        private static readonly GUIContent s_WarpedFlareIntensity = EditorGUIUtility.TrTextContent(
            "Warped Multiplier",
            "Controls the intensity of the warped flare sample.");
        private static readonly GUIContent s_WarpedFlareScale = EditorGUIUtility.TrTextContent(
            "Scale",
            "Sets the scale of the warped flare sample.");
        private static readonly GUIContent s_Samples = EditorGUIUtility.TrTextContent(
            "Samples",
            "Controls how many times the flare effect is repeated for each flare type.");
        private static readonly GUIContent s_SampleDimmer = EditorGUIUtility.TrTextContent(
            "Sample Dimmer",
            "Controls the multiplier applied to each additional sample.");
        private static readonly GUIContent s_VignetteEffect = EditorGUIUtility.TrTextContent(
            "Vignette Effect",
            "Controls the vignette used to occlude flares near the center of the screen.");
        private static readonly GUIContent s_StartingPosition = EditorGUIUtility.TrTextContent(
            "Starting Position",
            "Controls the starting position of the flares in screen space relative to their source.");
        private static readonly GUIContent s_Scale = EditorGUIUtility.TrTextContent(
            "Scale",
            "Controls the scale at which the flares are sampled.");
        private static readonly GUIContent s_StreaksIntensity = EditorGUIUtility.TrTextContent(
            "Multiplier",
            "Controls the intensity of the streaks effect.");
        private static readonly GUIContent s_StreaksLength = EditorGUIUtility.TrTextContent(
            "Length",
            "Controls the length of the streaks effect.");
        private static readonly GUIContent s_StreaksOrientation = EditorGUIUtility.TrTextContent(
            "Orientation",
            "Controls the orientation of the streaks effect in degrees.");
        private static readonly GUIContent s_StreaksThreshold = EditorGUIUtility.TrTextContent(
            "Threshold",
            "Controls the threshold of the streaks effect.");
        private static readonly GUIContent s_Resolution = EditorGUIUtility.TrTextContent(
            "Resolution",
            "Specifies the resolution ratio at which the streaks effect is computed.");
        private static readonly GUIContent s_SpectralLut = EditorGUIUtility.TrTextContent(
            "Spectral LUT",
            "Specifies a texture used to shift the hue of chromatic aberrations.");
        private static readonly GUIContent s_ChromaticAbberationIntensity = EditorGUIUtility.TrTextContent(
            "Intensity",
            "Controls the strength of the chromatic aberration effect.");
        private static readonly GUIContent s_ChromaticAbberationSampleCount = EditorGUIUtility.TrTextContent(
            "Samples",
            "Controls the number of samples used to render chromatic aberration.");

        private SerializedDataParameter m_Intensity;
        private SerializedDataParameter m_TintColor;
        private SerializedDataParameter m_BloomMip;
        private SerializedDataParameter m_FirstFlareIntensity;
        private SerializedDataParameter m_SecondaryFlareIntensity;
        private SerializedDataParameter m_WarpedFlareIntensity;
        private SerializedDataParameter m_WarpedFlareScale;
        private SerializedDataParameter m_Samples;
        private SerializedDataParameter m_SampleDimmer;
        private SerializedDataParameter m_VignetteEffect;
        private SerializedDataParameter m_StartingPosition;
        private SerializedDataParameter m_Scale;
        private SerializedDataParameter m_StreaksIntensity;
        private SerializedDataParameter m_StreaksLength;
        private SerializedDataParameter m_StreaksOrientation;
        private SerializedDataParameter m_StreaksThreshold;
        private SerializedDataParameter m_Resolution;
        private SerializedDataParameter m_SpectralLut;
        private SerializedDataParameter m_ChromaticAbberationIntensity;
        private SerializedDataParameter m_ChromaticAbberationSampleCount;

        public override bool hasAdditionalProperties => true;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<ScreenSpaceLensFlare>(serializedObject);

            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_TintColor = Unpack(o.Find(x => x.tintColor));
            m_BloomMip = Unpack(o.Find(x => x.bloomMip));
            m_FirstFlareIntensity = Unpack(o.Find(x => x.firstFlareIntensity));
            m_SecondaryFlareIntensity = Unpack(o.Find(x => x.secondaryFlareIntensity));
            m_WarpedFlareIntensity = Unpack(o.Find(x => x.warpedFlareIntensity));
            m_WarpedFlareScale = Unpack(o.Find(x => x.warpedFlareScale));
            m_Samples = Unpack(o.Find(x => x.samples));
            m_SampleDimmer = Unpack(o.Find(x => x.sampleDimmer));
            m_VignetteEffect = Unpack(o.Find(x => x.vignetteEffect));
            m_StartingPosition = Unpack(o.Find(x => x.startingPosition));
            m_Scale = Unpack(o.Find(x => x.scale));
            m_StreaksIntensity = Unpack(o.Find(x => x.streaksIntensity));
            m_StreaksLength = Unpack(o.Find(x => x.streaksLength));
            m_StreaksOrientation = Unpack(o.Find(x => x.streaksOrientation));
            m_StreaksThreshold = Unpack(o.Find(x => x.streaksThreshold));
            m_Resolution = Unpack(o.Find(x => x.resolution));
            m_SpectralLut = Unpack(o.Find(x => x.spectralLut));
            m_ChromaticAbberationIntensity = Unpack(o.Find(x => x.chromaticAbberationIntensity));
            m_ChromaticAbberationSampleCount = Unpack(o.Find(x => x.chromaticAbberationSampleCount));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Intensity, s_Intensity);
            PropertyField(m_TintColor, s_TintColor);
            PropertyField(m_BloomMip, s_BloomMip);

            PropertyField(m_FirstFlareIntensity, s_FirstFlareIntensity);
            PropertyField(m_SecondaryFlareIntensity, s_SecondaryFlareIntensity);
            PropertyField(m_WarpedFlareIntensity, s_WarpedFlareIntensity);
            if (showAdditionalProperties)
            {
                using (new IndentLevelScope())
                    PropertyField(m_WarpedFlareScale, s_WarpedFlareScale);
            }

            PropertyField(m_Samples, s_Samples);
            if (showAdditionalProperties)
            {
                using (new IndentLevelScope())
                    PropertyField(m_SampleDimmer, s_SampleDimmer);
            }

            PropertyField(m_VignetteEffect, s_VignetteEffect);
            PropertyField(m_StartingPosition, s_StartingPosition);
            PropertyField(m_Scale, s_Scale);

            PropertyField(m_StreaksIntensity, s_StreaksIntensity);
            using (new IndentLevelScope())
            {
                PropertyField(m_StreaksLength, s_StreaksLength);
                PropertyField(m_StreaksOrientation, s_StreaksOrientation);
                PropertyField(m_StreaksThreshold, s_StreaksThreshold);
                PropertyField(m_Resolution, s_Resolution);
            }

            PropertyField(m_SpectralLut, s_SpectralLut);
            PropertyField(m_ChromaticAbberationIntensity, s_ChromaticAbberationIntensity);
            PropertyField(m_ChromaticAbberationSampleCount, s_ChromaticAbberationSampleCount);
        }
    }
}
