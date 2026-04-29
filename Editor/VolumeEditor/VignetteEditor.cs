using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(Vignette))]
    internal sealed class VignetteEditor : VolumeComponentEditor
    {
        private SerializedDataParameter m_Mode;
        private SerializedDataParameter m_Color;
        private SerializedDataParameter m_Center;
        private SerializedDataParameter m_Intensity;
        private SerializedDataParameter m_Smoothness;
        private SerializedDataParameter m_Roundness;
        private SerializedDataParameter m_Rounded;
        private SerializedDataParameter m_Mask;
        private SerializedDataParameter m_Opacity;

        public override void OnEnable()
        {
            var o = new PropertyFetcher<Vignette>(serializedObject);

            m_Mode = Unpack(o.Find(x => x.mode));
            m_Color = Unpack(o.Find(x => x.color));
            m_Center = Unpack(o.Find(x => x.center));
            m_Intensity = Unpack(o.Find(x => x.intensity));
            m_Smoothness = Unpack(o.Find(x => x.smoothness));
            m_Roundness = Unpack(o.Find(x => x.roundness));
            m_Rounded = Unpack(o.Find(x => x.rounded));
            m_Mask = Unpack(o.Find(x => x.mask));
            m_Opacity = Unpack(o.Find(x => x.opacity));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_Mode);
            PropertyField(m_Color);

            if (m_Mode.value.intValue == (int)VignetteMode.Procedural)
            {
                PropertyField(m_Center);
                PropertyField(m_Intensity);
                PropertyField(m_Smoothness);
                PropertyField(m_Roundness);
                PropertyField(m_Rounded);
                return;
            }

            PropertyField(m_Mask);
            ValidateMaskImportSettings();
            PropertyField(m_Opacity);
        }

        private void ValidateMaskImportSettings()
        {
            var mask = m_Mask.value.objectReferenceValue as Texture2D;
            if (mask == null)
                return;

            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mask)) as TextureImporter;
            if (importer == null)
                return;

            var valid = importer.anisoLevel == 0
                && !importer.mipmapEnabled
                && importer.alphaSource == TextureImporterAlphaSource.FromGrayScale
                && importer.wrapMode == TextureWrapMode.Clamp;

            if (valid)
                return;

            EditorGUILayout.HelpBox(
                "Mask import settings should use grayscale alpha, clamp wrapping, no mipmaps, and no anisotropic filtering.",
                MessageType.Warning);
        }
    }
}
