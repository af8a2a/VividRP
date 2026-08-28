using System;
using UnityEditor;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    internal static class MaterialProgramCatalogBaker
    {
        private const string AssetRelativePath =
            "Runtime/Resources/VividMaterialProgramCatalog.asset";

        [NoAutoStaticsCleanup]
        private static bool s_IsBaking;

        internal static string AssetPath =>
            VividPackagePathUtility.GetPreferredAssetPath(AssetRelativePath);

        internal static MaterialProgramCatalogAsset BakeAll()
        {
            return Bake(
                GPUDrivenMaterialCompiler.ProgramCatalog,
                AssetPath,
                MaterialSurfaceHlslGenerator.GeneratedPath,
                MaterialCoverageHlslGenerator.GeneratedPath);
        }

        internal static MaterialProgramCatalogAsset Bake(
            MaterialProgramCatalog catalog,
            string assetPath,
            string surfaceHlslPath,
            string coverageHlslPath)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("A catalog asset path is required.", nameof(assetPath));
            if (string.IsNullOrEmpty(surfaceHlslPath))
                throw new ArgumentException("A Surface HLSL path is required.", nameof(surfaceHlslPath));
            if (string.IsNullOrEmpty(coverageHlslPath))
                throw new ArgumentException("A Coverage HLSL path is required.", nameof(coverageHlslPath));
            if (s_IsBaking)
                return AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(assetPath);

            s_IsBaking = true;
            try
            {
                MaterialProgramCatalogAsset asset =
                    AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                        assetPath);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<
                        MaterialProgramCatalogAsset>();
                    AssetDatabase.CreateAsset(asset, assetPath);
                }

                if (!asset.Matches(catalog, out _))
                {
                    asset.Apply(catalog);
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }

                MaterialSurfaceHlslGenerator.Generate(catalog, surfaceHlslPath);
                MaterialCoverageHlslGenerator.Generate(catalog, coverageHlslPath);
                return asset;
            }
            finally
            {
                s_IsBaking = false;
            }
        }

        internal static bool IsSynchronized()
        {
            MaterialProgramCatalog catalog =
                GPUDrivenMaterialCompiler.ProgramCatalog;
            MaterialProgramCatalogAsset asset =
                AssetDatabase.LoadAssetAtPath<MaterialProgramCatalogAsset>(
                    AssetPath);
            return asset != null
                && asset.Matches(catalog, out _)
                && MaterialSurfaceHlslGenerator.IsSynchronized(
                    catalog,
                    MaterialSurfaceHlslGenerator.GeneratedPath)
                && MaterialCoverageHlslGenerator.IsSynchronized(
                    catalog,
                    MaterialCoverageHlslGenerator.GeneratedPath);
        }

        [MenuItem("VividRP/GPU Driven/Bake Frozen Material Program Catalog")]
        private static void BakeFromMenu()
        {
            try
            {
                MaterialProgramCatalogAsset asset = BakeAll();
                Debug.Log(
                    $"Baked frozen Material Program Catalog at '{AssetDatabase.GetAssetPath(asset)}'.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
