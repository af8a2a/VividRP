using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [ScriptedImporter(3, MaterialGraphEditorGraph.AssetExtension)]
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
            asset.Apply(
                result,
                GPUDrivenMaterialCompiler.ProgramVersion,
                GPUDrivenMaterialCompiler.ProgramCatalog);

            ctx.AddObjectToAsset("MaterialGraph", asset);
            ctx.SetMainObject(asset);
        }
    }
}
