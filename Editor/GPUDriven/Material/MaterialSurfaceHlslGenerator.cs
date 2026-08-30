using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    internal static class MaterialSurfaceHlslGenerator
    {
        private const string GeneratedRelativePath =
            "Shaders/Core/Public/GPUDriven/VividMaterialSurfaceAOT.generated.hlsl";

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
            return MaterialSurfaceHlslSourceBuilder.BuildSource(catalog);
        }

        [MenuItem("VividRP/GPU Driven/Generate AOT Surface HLSL")]
        private static void GenerateFromMenu()
        {
            try
            {
                string generatedPath = GenerateAll();
                Debug.Log($"Generated AOT Surface HLSL at '{generatedPath}'.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    [InitializeOnLoad]
    internal static class MaterialSurfaceHlslGenerationBootstrap
    {
        static MaterialSurfaceHlslGenerationBootstrap()
        {
            EditorApplication.delayCall += Generate;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            EditorApplication.delayCall += Generate;
        }

        private static void Generate()
        {
            try
            {
                MaterialProgramCatalogBaker.BakeAll();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    internal sealed class MaterialSurfaceHlslBuildPreprocessor :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                MaterialProgramCatalogBaker.BakeAll(
                    synchronizeGraphImports: true);
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"Failed to generate AOT Material HLSL: {exception.Message}");
            }
        }
    }
}
