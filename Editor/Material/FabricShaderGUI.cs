using UnityEditor;
using UnityEngine;

namespace VividRP.Editor
{
    /// <summary>
    /// Material Inspector for <c>VividRP/Material/FabricSlab</c> (CottonWool &amp; Silk).
    /// Synchronizes double-sided GI, render queue, and base color/map aliases with LWGUI.
    /// </summary>
    public sealed class FabricShaderGUI : LWGUI.LWGUI
    {
        private static readonly int s_CullId = Shader.PropertyToID("_Cull");
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int s_MainTexId = Shader.PropertyToID("_MainTex");

        protected override bool ShowLogo => false;

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            SetupFabricMaterial(material);
        }

        public override void ValidateMaterial(Material material)
        {
            base.ValidateMaterial(material);
            SetupFabricMaterial(material);
            GPUDriven.GPUDrivenMaterialProxyAutoSyncService.QueueMaterial(material);
        }

        internal static void SetupFabricMaterial(Material material)
        {
            if (material == null)
                return;

            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

            bool doubleSided = material.HasProperty(s_CullId) && material.GetFloat(s_CullId) < 0.5f;
            material.doubleSidedGI = doubleSided;

            if (material.HasProperty(s_BaseColorId) && material.HasProperty(s_ColorId))
                material.SetColor(s_ColorId, material.GetColor(s_BaseColorId));
            if (material.HasProperty(s_BaseMapId) && material.HasProperty(s_MainTexId))
                material.SetTexture(s_MainTexId, material.GetTexture(s_BaseMapId));
        }
    }
}
