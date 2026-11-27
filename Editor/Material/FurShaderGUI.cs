using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    /// <summary>
    /// Material Inspector for <c>VividRP/Material/FurSlab</c> (shell-based Substrate fur).
    /// Ensures GPU Instancing is enabled for per-shell <c>_ShellHeight</c> draws and keeps render
    /// queue / GPUDriven proxy sync aligned with LWGUI.
    /// </summary>
    public sealed class FurShaderGUI : LWGUI.LWGUI
    {
        protected override bool ShowLogo => false;

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            SetupFurMaterial(material);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            SetupFurMaterial(material);
            GPUDriven.GPUDrivenMaterialProxyAutoSyncService.QueueMaterial(material);
        }

        public static void SetupFurMaterial(Material material)
        {
            if (material == null)
                return;

            material.enableInstancing = true;
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
    }
}
