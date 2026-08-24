using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    internal static class MaterialCoverageHlslGenerator
    {
        private const string GeneratedRelativePath =
            "Shaders/Core/Public/GPUDriven/VividMaterialCoverageAOT.generated.hlsl";

        [NoAutoStaticsCleanup]
        private static bool s_IsGenerating;

        internal static string GeneratedPath =>
            VividPackagePathUtility.GetPreferredAssetPath(GeneratedRelativePath);

        internal static string GenerateAll()
        {
            return Generate(
                MaterialSurfaceHlslGenerator.GetBuiltinCatalog(),
                GeneratedPath);
        }

        internal static string Generate(
            MaterialProgramCatalog catalog,
            string generatedPath)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            if (string.IsNullOrEmpty(generatedPath))
                throw new ArgumentException("A generated HLSL path is required.", nameof(generatedPath));
            if (s_IsGenerating)
                return generatedPath;

            s_IsGenerating = true;
            try
            {
                string source = BuildSource(catalog);
                string directory = Path.GetDirectoryName(generatedPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                bool sourceChanged = !File.Exists(generatedPath)
                    || !string.Equals(
                        File.ReadAllText(generatedPath),
                        source,
                        StringComparison.Ordinal);
                if (sourceChanged)
                {
                    File.WriteAllText(
                        generatedPath,
                        source,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    AssetDatabase.ImportAsset(
                        generatedPath,
                        ImportAssetOptions.ForceSynchronousImport);
                }

                return generatedPath;
            }
            finally
            {
                s_IsGenerating = false;
            }
        }

        internal static bool IsSynchronized()
        {
            return IsSynchronized(
                MaterialSurfaceHlslGenerator.GetBuiltinCatalog(),
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
                return string.Equals(
                        File.ReadAllText(generatedPath),
                    BuildSource(catalog),
                    StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static string BuildSource()
        {
            return BuildSource(MaterialSurfaceHlslGenerator.GetBuiltinCatalog());
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
