using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime;

namespace VividRP.Editor.RenderPipeline
{
    [CustomPropertyDrawer(typeof(VividDefaultVolumeProfileSettings))]
    internal sealed class VividDefaultVolumeProfileSettingsPropertyDrawer : DefaultVolumeProfileSettingsPropertyDrawer
    {
        private static readonly GUIContent s_ProfileLabel = EditorGUIUtility.TrTextContent("Default Volume Profile");
        private static readonly GUIContent s_InfoBoxLabel = EditorGUIUtility.TrTextContent(
            "This profile defines the baseline Volume state before scene Volumes are blended on top.");

        protected override GUIContent volumeInfoBoxLabel => s_InfoBoxLabel;
        protected override GUIContent defaultVolumeProfileAssetLabel => s_InfoBoxLabel;

        protected override VisualElement CreateAssetFieldUI()
        {
            var root = new VisualElement();
            var profileField = new PropertyField(m_VolumeProfileSerializedProperty, s_ProfileLabel.text);
            profileField.RegisterValueChangeCallback(_ => RefreshProfileEditor());
            root.Add(profileField);
            return root;
        }

        private void RefreshProfileEditor()
        {
            m_SettingsSerializedObject.ApplyModifiedProperties();
            DestroyDefaultVolumeProfileEditor();

            var profile = m_VolumeProfileSerializedProperty.objectReferenceValue as VolumeProfile;
            if (profile != null)
                VolumeProfileUtils.UpdateGlobalDefaultVolumeProfile<VividRenderPipeline>(profile);

            CreateDefaultVolumeProfileEditor();
        }
    }

    internal sealed class VividDefaultVolumeProfileSettingsContextMenu
        : DefaultVolumeProfileSettingsPropertyDrawer.DefaultVolumeProfileSettingsContextMenu2<
            VividDefaultVolumeProfileSettings,
            VividRenderPipeline>
    {
        protected override string defaultVolumeProfilePath => VividDefaultVolumeProfileEditorUtility.DefaultVolumeProfilePath;
    }
}
