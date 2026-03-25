using System;
using System.Collections.Generic;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Converter;
using UnityEngine;

namespace VividRP.Editor
{
    
    [Serializable]
    [PipelineConverter("Universal Render Pipeline", "VividRP")]
    internal sealed class URPLitMaterialConverter : RenderPipelineConverterMaterialUpgrader
    {
        protected override List<MaterialUpgrader> upgraders
        {
            get
            {
                var list = new List<MaterialUpgrader>();
                list.Add(CreateURPLitUpgrader());
                list.Add(CreateURPSimpleLitUpgrader());
                list.Add(CreateURPUnlitUpgrader());
                list.Add(CreateURPComplexLitUpgrader());
                return list;
            }
        }

        private static MaterialUpgrader CreateURPLitUpgrader()
        {
            var upgrader = new URPLitToStandardLitUpgrader();
            upgrader.RenameShader(
                "Universal Render Pipeline/Lit",
                StandardLitMaterialImportUtility.StandardLitShaderName,
                StandardLitMaterialUtility.SetupMaterialFinalizer);

            // Textures — URP Lit and VividRP StandardLit share property names for most textures.
            upgrader.RenameTexture("_BaseMap", "_BaseMap");
            upgrader.RenameTexture("_BumpMap", "_BumpMap");
            upgrader.RenameTexture("_OcclusionMap", "_OcclusionMap");
            upgrader.RenameTexture("_EmissionMap", "_EmissionMap");
            upgrader.RenameTexture("_MetallicGlossMap", "_MetallicGlossMap");

            // Colors
            upgrader.RenameColor("_BaseColor", "_BaseColor");
            upgrader.RenameColor("_EmissionColor", "_EmissionColor");

            // Floats
            upgrader.RenameFloat("_Metallic", "_Metallic");
            upgrader.RenameFloat("_Smoothness", "_Smoothness");
            upgrader.RenameFloat("_BumpScale", "_BumpScale");
            upgrader.RenameFloat("_OcclusionStrength", "_OcclusionStrength");
            upgrader.RenameFloat("_Cutoff", "_Cutoff");
            upgrader.RenameFloat("_AlphaClip", "_AlphaClip");
            upgrader.RenameFloat("_SmoothnessTextureChannel", "_SmoothnessTextureChannel");
            upgrader.RenameFloat("_ClearCoatMask", "_ClearCoatMask");
            upgrader.RenameFloat("_ClearCoatSmoothness", "_ClearCoatSmoothness");
            upgrader.RenameFloat("_Cull", "_Cull");
            upgrader.RenameFloat("_QueueOffset", "_QueueOffset");
            upgrader.RenameFloat("_ReceiveShadows", "_ReceiveShadows");

            // Workflow mode: URP uses 0=Specular, 1=Metallic — same as VividRP.
            upgrader.RenameFloat("_WorkflowMode", "_WorkflowMode");

            return upgrader;
        }

        private static MaterialUpgrader CreateURPSimpleLitUpgrader()
        {
            var upgrader = new MaterialUpgrader();
            upgrader.RenameShader(
                "Universal Render Pipeline/Simple Lit",
                StandardLitMaterialImportUtility.StandardLitShaderName,
                StandardLitMaterialUtility.SetupMaterialFinalizer);

            upgrader.RenameTexture("_BaseMap", "_BaseMap");
            upgrader.RenameTexture("_BumpMap", "_BumpMap");
            upgrader.RenameTexture("_EmissionMap", "_EmissionMap");

            upgrader.RenameColor("_BaseColor", "_BaseColor");
            upgrader.RenameColor("_EmissionColor", "_EmissionColor");

            upgrader.RenameFloat("_Cutoff", "_Cutoff");
            upgrader.RenameFloat("_AlphaClip", "_AlphaClip");
            upgrader.RenameFloat("_BumpScale", "_BumpScale");
            upgrader.RenameFloat("_Cull", "_Cull");
            upgrader.RenameFloat("_QueueOffset", "_QueueOffset");
            upgrader.RenameFloat("_ReceiveShadows", "_ReceiveShadows");

            // Simple Lit has no metallic/smoothness workflow — set sensible defaults.
            upgrader.SetFloat("_WorkflowMode", StandardLitMaterialUtility.MetallicWorkflow);
            upgrader.SetFloat("_Metallic", 0.0f);
            upgrader.SetFloat("_Smoothness", 0.5f);

            return upgrader;
        }

        private static MaterialUpgrader CreateURPUnlitUpgrader()
        {
            var upgrader = new MaterialUpgrader();
            upgrader.RenameShader(
                "Universal Render Pipeline/Unlit",
                StandardLitMaterialImportUtility.StandardLitShaderName,
                StandardLitMaterialUtility.SetupMaterialFinalizer);

            upgrader.RenameTexture("_BaseMap", "_BaseMap");
            upgrader.RenameColor("_BaseColor", "_BaseColor");
            upgrader.RenameFloat("_Cutoff", "_Cutoff");
            upgrader.RenameFloat("_AlphaClip", "_AlphaClip");
            upgrader.RenameFloat("_Cull", "_Cull");
            upgrader.RenameFloat("_QueueOffset", "_QueueOffset");

            // Unlit → set default PBR values.
            upgrader.SetFloat("_WorkflowMode", StandardLitMaterialUtility.MetallicWorkflow);
            upgrader.SetFloat("_Metallic", 0.0f);
            upgrader.SetFloat("_Smoothness", 0.5f);

            return upgrader;
        }

        private static MaterialUpgrader CreateURPComplexLitUpgrader()
        {
            var upgrader = new URPLitToStandardLitUpgrader();
            upgrader.RenameShader(
                "Universal Render Pipeline/Complex Lit",
                StandardLitMaterialImportUtility.StandardLitShaderName,
                StandardLitMaterialUtility.SetupMaterialFinalizer);

            // Complex Lit shares the same property names as Lit.
            upgrader.RenameTexture("_BaseMap", "_BaseMap");
            upgrader.RenameTexture("_BumpMap", "_BumpMap");
            upgrader.RenameTexture("_OcclusionMap", "_OcclusionMap");
            upgrader.RenameTexture("_EmissionMap", "_EmissionMap");
            upgrader.RenameTexture("_MetallicGlossMap", "_MetallicGlossMap");

            upgrader.RenameColor("_BaseColor", "_BaseColor");
            upgrader.RenameColor("_EmissionColor", "_EmissionColor");

            upgrader.RenameFloat("_Metallic", "_Metallic");
            upgrader.RenameFloat("_Smoothness", "_Smoothness");
            upgrader.RenameFloat("_BumpScale", "_BumpScale");
            upgrader.RenameFloat("_OcclusionStrength", "_OcclusionStrength");
            upgrader.RenameFloat("_Cutoff", "_Cutoff");
            upgrader.RenameFloat("_AlphaClip", "_AlphaClip");
            upgrader.RenameFloat("_SmoothnessTextureChannel", "_SmoothnessTextureChannel");
            upgrader.RenameFloat("_ClearCoatMask", "_ClearCoatMask");
            upgrader.RenameFloat("_ClearCoatSmoothness", "_ClearCoatSmoothness");
            upgrader.RenameFloat("_Cull", "_Cull");
            upgrader.RenameFloat("_QueueOffset", "_QueueOffset");
            upgrader.RenameFloat("_ReceiveShadows", "_ReceiveShadows");
            upgrader.RenameFloat("_WorkflowMode", "_WorkflowMode");

            return upgrader;
        }
    }

    /// <summary>
    /// Custom upgrader for URP Lit → VividRP StandardLit that handles the
    /// _SpecGlossMap → _MetallicGlossMap fallback when in specular workflow.
    /// </summary>
    internal sealed class URPLitToStandardLitUpgrader : MaterialUpgrader
    {
        public override void Convert(Material srcMaterial, Material dstMaterial)
        {
            base.Convert(srcMaterial, dstMaterial);

            // URP Lit uses _SpecGlossMap in specular workflow; VividRP only supports metallic,
            // but we copy the specular gloss map to the metallic map slot as a best-effort fallback.
            if (srcMaterial.HasProperty("_SpecGlossMap") && srcMaterial.GetTexture("_SpecGlossMap") != null)
            {
                if (dstMaterial.HasProperty("_MetallicGlossMap") && dstMaterial.GetTexture("_MetallicGlossMap") == null)
                {
                    dstMaterial.SetTexture("_MetallicGlossMap", srcMaterial.GetTexture("_SpecGlossMap"));
                }
            }

            // URP detail maps (_DetailAlbedoMap, _DetailNormalMap) are not supported in VividRP;
            // no conversion needed — they are simply dropped.
        }
    }
}
