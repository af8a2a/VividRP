using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering.Universal
{
    using CED = CoreEditorDrawer<SerializedLocalVolumetricFog>;

    static partial class LocalVolumetricFogUI
    {
        // also used for AdvancedModes
        [System.Flags]
        enum Expandable
        {
            FogTexture = 1 << 0,
            FogMaterial = 1 << 1,
        }

        readonly static ExpandedState<Expandable, LocalVolumetricFog> k_ExpandedState = new ExpandedState<Expandable, LocalVolumetricFog>(
            Expandable.FogTexture |
            Expandable.FogMaterial, "Vivid");

        public static readonly CED.IDrawer Inspector = CED.Group(
            CED.Group(Drawer_ToolBar, Drawer_PrimarySettings, Drawer_VolumeContent),
            CED.space,
            CED.Conditional((serialized, owner) => (LocalVolumetricFogMode)serialized.fogMode.intValue == LocalVolumetricFogMode.Texture,
                CED.FoldoutGroup(Styles.s_FogTextureHeader, Expandable.FogTexture, k_ExpandedState, Drawer_FogTexture)),
            CED.Conditional((serialized, owner) => (LocalVolumetricFogMode)serialized.fogMode.intValue == LocalVolumetricFogMode.Material,
                CED.FoldoutGroup(Styles.s_FogMaterialHeader, Expandable.FogMaterial, k_ExpandedState, Drawer_FogMaterial))
        );

        static void Drawer_ToolBar(SerializedLocalVolumetricFog serialized, Editor owner)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditMode.DoInspectorToolbar(new[] { LocalVolumetricFogEditor.k_EditShape, LocalVolumetricFogEditor.k_EditBlend },
                Styles.s_Toolbar_Contents, () =>
                {
                    var bounds = new Bounds();
                    foreach (Component targetObject in owner.targets)
                    {
                        bounds.Encapsulate(targetObject.transform.position);
                    }

                    return bounds;
                },
                owner);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static void Drawer_PrimarySettings(SerializedLocalVolumetricFog serialized, Editor owner)
        {
            EditorGUILayout.PropertyField(serialized.albedo, Styles.s_AlbedoLabel);
            EditorGUILayout.PropertyField(serialized.meanFreePath, Styles.s_MeanFreePathLabel);
            EditorGUILayout.PropertyField(serialized.priority, Styles.s_PriorityLabel);

            EditorGUILayout.PropertyField(serialized.fogMode, Styles.s_FogModeLabel);
        }

        static void Drawer_VolumeContent(SerializedLocalVolumetricFog serialized, Editor owner)
        {
            EditorGUILayout.PropertyField(serialized.size, Styles.s_Size);

            Vector3 serializedSize = serialized.size.vector3Value;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serialized.editorUniformFade, Styles.s_BlendLabel);
            if (EditorGUI.EndChangeCheck())
            {
                float max = Mathf.Min(serializedSize.x, serializedSize.y, serializedSize.z);
                serialized.editorUniformFade.floatValue = Mathf.Clamp(serialized.editorUniformFade.floatValue, 0f, max);
            }
        }

        static void Drawer_FogTexture(SerializedLocalVolumetricFog serialized, Editor owner)
        {
            EditorGUILayout.PropertyField(serialized.fogTexture, Styles.s_FogTextureLabel);
            EditorGUILayout.PropertyField(serialized.textureScrollingSpeed, Styles.s_TextureScrollLabel);
            EditorGUILayout.PropertyField(serialized.textureTiling, Styles.s_TextureTileLabel);
        }

        static void Drawer_FogMaterial(SerializedLocalVolumetricFog serialized, Editor owner)
        {
            EditorGUILayout.PropertyField(serialized.fogMaterial, Styles.s_FogMaterialLabel);
        }
    }
}