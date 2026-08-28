using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [Serializable]
    [Graph(
        AssetExtension,
        GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    internal sealed class MaterialGraphEditorGraph : Graph
    {
        internal const string AssetExtension = "vmatg";
        private const string DefaultGraphName = "Vivid Material Graph";

        [MenuItem("Assets/Create/VividRP/Material Graph", false)]
        private static void CreateAssetFile()
        {
            GraphDatabase.PromptInProjectBrowserToCreateNewAsset<MaterialGraphEditorGraph>(
                DefaultGraphName);
        }

        public override void OnGraphChanged(GraphLogger infos)
        {
            base.OnGraphChanged(infos);
            if (infos == null)
                return;

            MaterialGraphCompilationResult result =
                MaterialGraphEditorCompiler.Compile(this);
            var nodesById = new Dictionary<string, INode>(StringComparer.Ordinal);
            foreach (INode node in GetNodes())
                nodesById[node.ID.ToString()] = node;

            foreach (MaterialGraphDiagnostic diagnostic in result.Diagnostics)
            {
                nodesById.TryGetValue(diagnostic.SourceNodeId, out INode context);
                string message = $"[{diagnostic.Code}] {diagnostic.Message}";
                switch (diagnostic.Severity)
                {
                    case MaterialIRDiagnosticSeverity.Info:
                        infos.Log(message, context);
                        break;
                    case MaterialIRDiagnosticSeverity.Warning:
                        infos.LogWarning(message, context);
                        break;
                    default:
                        infos.LogError(message, context);
                        break;
                }
            }
        }
    }

    [ScriptedImporter(1, MaterialGraphEditorGraph.AssetExtension)]
    internal sealed class MaterialGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            MaterialGraphEditorGraph graph =
                GraphDatabase.LoadGraphForImporter<MaterialGraphEditorGraph>(ctx.assetPath);
            if (graph == null)
            {
                Debug.LogError($"Failed to load Material Graph asset: {ctx.assetPath}");
                return;
            }

            MaterialGraphCompilationResult result =
                MaterialGraphEditorCompiler.Compile(graph);
            var asset = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            asset.Apply(result, GPUDrivenMaterialCompiler.ProgramVersion);

            ctx.AddObjectToAsset("MaterialGraph", asset);
            ctx.SetMainObject(asset);
        }
    }
}
