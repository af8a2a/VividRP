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
                "Experimental Closure material. Stage 3 shades the Closure Buffer directly with directional shadows, clustered punctual and area lights, reflection probes, sky IBL, GTAO, and optional SSR consumption. Closure-native SSR production, transmission refraction, and profile-based subsurface scattering are not implemented yet.",
                MessageType.Info);
            base.OnGUI(materialEditor, properties);
        }
    }
}
