using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using Unity.Scripting.LifecycleManagement;
using VividRP.Runtime.GPUDriven;

namespace VividRP.Editor
{
    internal static class MaterialSurfaceHlslGenerator
    {
        private const string GeneratedRelativePath =
            "Shaders/Core/Public/GPUDriven/VividMaterialSurfaceAOT.generated.hlsl";

        [NoAutoStaticsCleanup]
        private static bool s_IsGenerating;

        internal static string GeneratedPath =>
            VividPackagePathUtility.GetPreferredAssetPath(GeneratedRelativePath);

        internal static string GenerateAll()
        {
            return Generate(GetBuiltinCatalog(), GeneratedPath);
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
            return IsSynchronized(GetBuiltinCatalog(), GeneratedPath);
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
            return BuildSource(GetBuiltinCatalog());
        }

        internal static string BuildSource(
            MaterialProgramCatalog catalog)
        {
            return MaterialSurfaceHlslSourceBuilder.BuildSource(catalog);
        }

        internal static MaterialProgramCatalog GetBuiltinCatalog()
        {
            return GPUDrivenMaterialCompiler.ProgramCatalog;
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
                MaterialProgramCatalogBaker.BakeAll();
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"Failed to generate AOT Material HLSL: {exception.Message}");
            }
        }
    }
}
