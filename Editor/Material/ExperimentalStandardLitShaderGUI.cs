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
                "Experimental Closure material. It currently validates the StandardSurface to SlabClosure path while exporting the legacy GBuffer.",
                MessageType.Info);
            base.OnGUI(materialEditor, properties);
        }
    }
}
