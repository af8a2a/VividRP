using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditorInternal;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [CustomEditor(typeof(VividSlabShaderImporter))]
    internal sealed class VividSlabShaderImporterEditor : ScriptedImporterEditor
    {
        private const string GeneratedCacheDirectory = "Library/VividRP/GeneratedSlabShaders";

        public override void OnInspectorGUI()
        {
            var importer = target as VividSlabShaderImporter;
            if (importer == null)
            {
                base.OnInspectorGUI();
                return;
            }

            string assetPath = importer.assetPath;
            VividSlabProgramDescriptor descriptor = LoadDescriptor(assetPath);

            EditorGUILayout.LabelField("VividRP Substrate Slab Shader (Phase S2)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Asset Path", assetPath);
                if (descriptor != null)
                {
                    EditorGUILayout.TextField("Shader Name", descriptor.ShaderName);
                    EditorGUILayout.ObjectField("Main Raster Shader", descriptor.RasterShader, typeof(Shader), false);
                    EditorGUILayout.ObjectField("GPUDriven Resolve Compute", descriptor.ResolveComputeShader, typeof(ComputeShader), false);
                    EditorGUILayout.TextField("Resolve Kernel", descriptor.ResolveKernelName);
                    EditorGUILayout.TextField(
                        "Layout Summary",
                        $"{descriptor.Parameters.Count} params ({descriptor.ParameterLaneCount * 16} B), {descriptor.ResourceCount} textures, ~{descriptor.EstimatedSubstrateBytesPerPixel} B/px Substrate");
                    EditorGUILayout.TextField(
                        "Substrate Closure",
                        descriptor.IsMultiClosure
                            ? $"Multi-Closure Tier 3 ({descriptor.ClosureCount} closures)"
                            : "Single-Closure (Tier 1/2)");
                    EditorGUILayout.TextField(
                        "Features",
                        $"GraphSurface={descriptor.HasLinkedGraphSurface}, PostHook={descriptor.HasCustomSurfaceHook}, CustomCoverage={descriptor.HasCustomCoverage}, WPO={descriptor.HasVertexOffset}, ProgVisRaster={descriptor.RequiresProgrammableVisibilityRaster}");
                    EditorGUILayout.TextField("Content Hash", $"0x{descriptor.SourceContentHash:X8}");
                    if (!string.IsNullOrEmpty(descriptor.BaseTemplateAssetPath))
                        EditorGUILayout.TextField("Base Template", descriptor.BaseTemplateAssetPath);
                    if (!string.IsNullOrEmpty(descriptor.LinkedGraphAssetPath))
                        EditorGUILayout.TextField("Linked MaterialGraph", descriptor.LinkedGraphAssetPath);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("View Generated Raster Shader"))
            {
                OpenGeneratedCode(assetPath, isCompute: false);
            }
            if (GUILayout.Button("View Generated GPUDriven Compute"))
            {
                OpenGeneratedCode(assetPath, isCompute: true);
            }
            EditorGUILayout.EndHorizontal();

            if (descriptor != null && !string.IsNullOrEmpty(descriptor.LinkedGraphAssetPath))
            {
                if (GUILayout.Button("Open Linked Material Graph"))
                {
                    Object graphAsset = AssetDatabase.LoadMainAssetAtPath(descriptor.LinkedGraphAssetPath);
                    if (graphAsset != null)
                        AssetDatabase.OpenAsset(graphAsset);
                }
            }

            if (descriptor != null && descriptor.Diagnostics.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    string.Join("\n", descriptor.Diagnostics),
                    descriptor.HasImportErrors ? MessageType.Error : MessageType.Warning);
            }

            ApplyRevertGUI();
        }

        [MenuItem("Assets/Create/VividRP/Substrate Slab Shader", false)]
        private static void CreateNewSlabShaderAsset()
        {
            const string defaultContent =
                "// @vivid-base \"Packages/com.vivid.render-pipelines/Shaders/Material/Templates/SubstrateOpaque.slab.template\"\n"
                + "// @vivid-graph \"Packages/com.vivid.render-pipelines/Runtime/Resources/MaterialGraphs/OpenPBROpaque.vmatg\"\n"
                + "\n"
                + "Shader \"VividRP/Material/NewSubstrateSlab\"\n"
                + "{\n"
                + "    Properties\n"
                + "    {\n"
                + "        VIVID_SLAB_GRAPH_PROPERTIES\n"
                + "    }\n"
                + "\n"
                + "    // @vivid-surface-begin\n"
                + "    // Optional OpenPBR V2 post-hook runs identically in Raster GBuffer and GPUDriven Compute.\n"
                + "    #define VIVID_SLAB_HAS_CUSTOM_POST_SURFACE 1\n"
                + "    void VividSlabPostEvaluateSurface(\n"
                + "        const VividSlabEvalContext ctx,\n"
                + "        inout VividOpenPBRV2Inputs v2,\n"
                + "        inout float ambientOcclusion)\n"
                + "    {\n"
                + "        // Example: enable physical clearcoat (automatically routes Substrate to Tier 2)\n"
                + "        // v2.coatWeight = 0.5f;\n"
                + "        // v2.coatRoughness = 0.05f;\n"
                + "    }\n"
                + "    // @vivid-surface-end\n"
                + "\n"
                + "    VIVID_SLAB_USE_BASE_SUBSHADER\n"
                + "}\n";

            ProjectWindowUtil.CreateAssetWithTextContent("NewSubstrateSlab.slab.shader", defaultContent);
        }

        private static VividSlabProgramDescriptor LoadDescriptor(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is VividSlabProgramDescriptor descriptor)
                    return descriptor;
            }
            return null;
        }

        private static void OpenGeneratedCode(string assetPath, bool isCompute)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                return;

            string sourceText = File.ReadAllText(assetPath);
            VividSlabShaderAssemblyResult assembled =
                VividSlabShaderImporter.AssembleFromAssetPath(assetPath, sourceText, ctx: null);

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
                guid = Path.GetFileNameWithoutExtension(assetPath);

            Directory.CreateDirectory(GeneratedCacheDirectory);
            string ext = isCompute ? ".generated.compute" : ".generated.shader";
            string outPath = Path.GetFullPath(
                Path.Combine(GeneratedCacheDirectory, $"{guid}{ext}"));
            File.WriteAllText(
                outPath,
                isCompute ? assembled.ComputeResolveSource : assembled.RasterShaderSource);
            InternalEditorUtility.OpenFileAtLineExternal(outPath, 1);
        }
    }
}
