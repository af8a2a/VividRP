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
                "Experimental Closure material. The Stage 2 Closure Buffer preserves IOR, coat, transmission and subsurface semantics and shades the Slab directly. The legacy GBuffer still degrades these fields; experimental deferred transmission has no refraction yet, and subsurface currently uses a wrap-diffuse approximation.",
                MessageType.Info);
            base.OnGUI(materialEditor, properties);
        }
    }
}
