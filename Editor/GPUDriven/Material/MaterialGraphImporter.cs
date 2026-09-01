using System;
using Unity.GraphToolkit.Editor;
using UnityEditor.AssetImporters;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor.GPUDriven
{
    [ScriptedImporter(6, MaterialGraphEditorGraph.AssetExtension)]
    internal sealed class MaterialGraphImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            ctx.DependsOnCustomDependency(
                MaterialProgramCatalogBaker.CatalogDependencyName);
            var asset = ScriptableObject.CreateInstance<MaterialGraphImportAsset>();
            try
            {
                MaterialGraphEditorGraph graph =
                    GraphDatabase.LoadGraphForImporter<MaterialGraphEditorGraph>(
                        ctx.assetPath);
                if (graph == null)
                {
                    ApplyImportFailure(
                        ctx,
                        asset,
                        $"Failed to load Material Graph asset '{ctx.assetPath}'.");
                }
                else
                {
                    MaterialGraphCompilationResult result =
                        MaterialGraphEditorCompiler.Compile(graph);
                    MaterialProgramCatalogAsset frozenCatalog =
                        MaterialProgramCatalogAsset.LoadDefault();
                    if (!TryValidateFrozenCatalog(
                            frozenCatalog,
                            out string catalogFailure))
                    {
                        ApplyImportFailure(ctx, asset, catalogFailure);
                    }
                    else
                    {
                        asset.Apply(
                            result,
                            GPUDrivenMaterialCompiler.ProgramVersion,
                            frozenCatalog);
                    }
                }
            }
            catch (Exception exception)
            {
                ApplyImportFailure(
                    ctx,
                    asset,
                    $"Material Graph import failed for '{ctx.assetPath}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }

            ctx.AddObjectToAsset("MaterialGraph", asset);
            ctx.SetMainObject(asset);
        }

        private static bool TryValidateFrozenCatalog(
            MaterialProgramCatalogAsset frozenCatalog,
            out string failure)
        {
            if (frozenCatalog == null)
            {
                failure =
                    "Frozen Material Program Catalog is unavailable. Bake the catalog before importing Material Graphs.";
                return false;
            }
            if (!frozenCatalog.IsCommitted)
            {
                failure =
                    "Frozen Material Program Catalog has not committed a complete artifact set.";
                return false;
            }
            if (!frozenCatalog.ArtifactSetHash.IsValid)
            {
                failure =
                    "Frozen Material Program Catalog has an invalid artifact-set stamp.";
                return false;
            }
            if (!frozenCatalog.ExtendsBuiltinCatalog(
                    GPUDrivenMaterialCompiler.ProgramCatalog,
                    out string catalogFailure))
            {
                failure =
                    $"Frozen Material Program Catalog is incompatible: {catalogFailure}";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        private static void ApplyImportFailure(
            AssetImportContext ctx,
            MaterialGraphImportAsset asset,
            string message)
        {
            string diagnostic = $"MAT-IMPORT: {message}";
            asset.ApplyFailure(
                GPUDrivenMaterialCompiler.ProgramVersion,
                diagnostic);
            ctx.LogImportError(diagnostic);
        }
    }
}
