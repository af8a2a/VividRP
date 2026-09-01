using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    internal static class MaterialCoverageHlslGenerator
    {
        private const string GeneratedRelativePath =
            "Shaders/Core/Public/GPUDriven/VividMaterialCoverageAOT.generated.hlsl";

        internal static string GeneratedPath =>
            VividPackagePathUtility.GetPreferredAssetPath(GeneratedRelativePath);

        internal static string GenerateAll()
        {
            MaterialProgramCatalogBaker.BakeAll();
            return GeneratedPath;
        }

        internal static bool IsSynchronized()
        {
            return IsSynchronized(
                MaterialProgramCatalogBaker.CurrentCatalog,
                GeneratedPath);
        }

        internal static bool IsSynchronized(
            MaterialProgramCatalog catalog,
            string generatedPath)
        {
            if (catalog == null
                || string.IsNullOrEmpty(generatedPath)
                || !File.Exists(generatedPath))
            {
                return false;
            }

            try
            {
                string stampPath = Path.Combine(
                        Path.GetDirectoryName(generatedPath) ?? string.Empty,
                        MaterialProgramArtifactSetHlslContract
                            .PublishedStampFileName)
                    .Replace('\\', '/');
                return string.Equals(
                        File.ReadAllText(generatedPath),
                        BuildSource(catalog),
                        StringComparison.Ordinal)
                    && File.Exists(stampPath)
                    && string.Equals(
                        File.ReadAllText(stampPath),
                        MaterialProgramCatalogHlslStampSourceBuilder.BuildSource(
                            catalog),
                        StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string BuildSource()
        {
            return BuildSource(MaterialProgramCatalogBaker.CurrentCatalog);
        }

        internal static string BuildSource(
            MaterialProgramCatalog catalog)
        {
            return MaterialCoverageHlslSourceBuilder.BuildSource(catalog);
        }

        [MenuItem("VividRP/GPU Driven/Generate AOT Coverage HLSL")]
        private static void GenerateFromMenu()
        {
            try
            {
                string generatedPath = GenerateAll();
                Debug.Log($"Generated AOT Coverage HLSL at '{generatedPath}'.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
