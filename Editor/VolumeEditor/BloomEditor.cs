using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Bloom))]
    internal sealed class BloomEditor : VolumeComponentEditor
    {
        private static readonly GUIContent s_Mode = EditorGUIUtility.TrTextContent(
            "Mode",
            "Selects the fast mip-pyramid scattering path or image-space FFT convolution.");

        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_Threshold;
        private SerializedDataParameter m_Intensity;
        private SerializedDataParameter m_Scatter;
        private SerializedDataParameter m_Tint;
        private SerializedDataParameter m_DirtTexture;
        private SerializedDataParameter m_DirtIntensity;
        private SerializedDataParameter m_Anamorphic;
        private SerializedDataParameter m_Resolution;
        private SerializedDataParameter m_HighQualityPrefiltering;
        private SerializedDataParameter m_HighQualityFiltering;
        private SerializedDataParameter m_ExperimentalSpdDownsample;
        private SerializedDataParameter m_ConvolutionKernel;
        private SerializedDataParameter m_ConvolutionSize;
        private SerializedDataParameter m_ConvolutionBufferScale;
        private SerializedDataParameter m_ConvolutionCenter;
        private SerializedDataParameter m_ConvolutionKernelClamp;
        private SerializedDataParameter m_ConvolutionResolutionScale;

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<Bloom>(serializedObject);
            m_Mode = Unpack(fetcher.Find(x => x.mode));
            m_Threshold = Unpack(fetcher.Find(x => x.threshold));
            m_Intensity = Unpack(fetcher.Find(x => x.intensity));
            m_Scatter = Unpack(fetcher.Find(x => x.scatter));
            m_Tint = Unpack(fetcher.Find(x => x.tint));
            m_DirtTexture = Unpack(fetcher.Find(x => x.dirtTexture));
            m_DirtIntensity = Unpack(fetcher.Find(x => x.dirtIntensity));
            m_Anamorphic = Unpack(fetcher.Find(x => x.anamorphic));
            m_Resolution = Unpack(fetcher.Find(x => x.resolution));
            m_HighQualityPrefiltering = Unpack(
                fetcher.Find(x => x.highQualityPrefiltering));
            m_HighQualityFiltering = Unpack(
                fetcher.Find(x => x.highQualityFiltering));
            m_ExperimentalSpdDownsample = Unpack(
                fetcher.Find(x => x.experimentalSpdDownsample));
            m_ConvolutionKernel = Unpack(fetcher.Find(x => x.convolutionKernel));
            m_ConvolutionSize = Unpack(fetcher.Find(x => x.convolutionSize));
            m_ConvolutionBufferScale = Unpack(
                fetcher.Find(x => x.convolutionBufferScale));
            m_ConvolutionCenter = Unpack(fetcher.Find(x => x.convolutionCenter));
            m_ConvolutionKernelClamp = Unpack(
                fetcher.Find(x => x.convolutionKernelClamp));
            m_ConvolutionResolutionScale = Unpack(
                fetcher.Find(x => x.convolutionResolutionScale));
        }

        public override void OnInspectorGUI()
        {
            DrawSectionHeader("General");
            PropertyField(m_Mode, s_Mode);
            PropertyField(m_Intensity);
            PropertyField(m_Threshold);
            PropertyField(m_Tint);

            if (m_Mode.value.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "Select a common Bloom mode to edit path-specific settings.",
                    MessageType.Info);
            }
            else
            {
                var mode = (BloomMode)m_Mode.value.intValue;
                if (UsesScatteringSettings(mode))
                    DrawScatteringSettings();
                else if (UsesConvolutionSettings(mode))
                    DrawConvolutionSettings();
            }

            DrawLensDirtSettings();
        }

        internal static bool UsesScatteringSettings(BloomMode mode)
        {
            return mode == BloomMode.Scattering;
        }

        internal static bool UsesConvolutionSettings(BloomMode mode)
        {
            return mode == BloomMode.ConvolutionFFT;
        }

        private void DrawScatteringSettings()
        {
            DrawSectionHeader("Scattering");
            PropertyField(m_Scatter);
            PropertyField(m_Anamorphic);
            PropertyField(m_Resolution);
            PropertyField(m_HighQualityPrefiltering);
            PropertyField(m_HighQualityFiltering);
            PropertyField(m_ExperimentalSpdDownsample);
        }

        private void DrawConvolutionSettings()
        {
            DrawSectionHeader("Convolution FFT");
            PropertyField(m_ConvolutionKernel);
            PropertyField(m_ConvolutionResolutionScale);
            PropertyField(m_ConvolutionSize);
            PropertyField(m_ConvolutionBufferScale);
            PropertyField(m_ConvolutionCenter);
            PropertyField(m_ConvolutionKernelClamp);
        }

        private void DrawLensDirtSettings()
        {
            DrawSectionHeader("Lens Dirt");
            PropertyField(m_DirtTexture);
            PropertyField(m_DirtIntensity);
        }

        private static void DrawSectionHeader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
