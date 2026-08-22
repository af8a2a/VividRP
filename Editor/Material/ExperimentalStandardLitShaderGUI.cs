using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    public sealed class ExperimentalStandardLitShaderGUI : LWGUI.LWGUI
    {
        protected override bool ShowLogo => false;

        public override void AssignNewShaderToMaterial(
            Material material,
            Shader oldShader,
            Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            StandardLitMaterialUtility.SetupMaterial(material, oldShader, true);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            StandardLitMaterialUtility.SetupMaterial(material, null, true);
        }

        public override void OnGUI(
            MaterialEditor materialEditor,
            MaterialProperty[] properties)
        {
            EditorGUILayout.HelpBox(
                "Experimental Closure material. Stage 4 supports a two-Slab top layer with Horizontal Mix and Vertical Layer operators. The compact top layer shares the base normal; arbitrary Closure trees, Closure-native SSR production, transmission refraction, and profile-based subsurface scattering are not implemented yet.",
                MessageType.Info);
            base.OnGUI(materialEditor, properties);
        }
    }
}
