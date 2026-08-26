using System;
using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;
using VividRP.Editor.MeshShader;
using VividRP.Runtime.MeshShader;

namespace VividRP.Editor.Tests
{
    internal sealed class VividMeshShaderProgramImporterTests
    {
        private const string CanonicalSourcePath =
            "Packages/com.vivid.render-pipelines/Shaders/Core/Private/GPUDriven/VisibilityBufferMeshShader.hlsl";

        [Test]
        public void CompilerInteropLayout_MatchesNativeX64Abi()
        {
            if (IntPtr.Size != 8)
                Assert.Ignore("VividMeshShaderCompiler currently targets x64 editors.");

            Assert.That(VividMeshShaderCompiler.HasExpectedAbiLayout(), Is.True);
        }

        [Test]
        public void PackageAliases_ResolveToTheInstalledVividPackage()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(
                typeof(VividMeshShaderProgramAsset).Assembly);
            Assert.That(packageInfo, Is.Not.Null);

            string installedManifestAssetPath =
                $"{packageInfo.assetPath}/Runtime/Resources/VividMeshShader/VisibilityBufferMeshShader.vms";
            Assert.That(
                VividMeshShaderProgramImporter.TryResolvePhysicalAssetPath(
                    installedManifestAssetPath,
                    out string manifestPhysicalPath),
                Is.True);
            Assert.That(File.Exists(manifestPhysicalPath), Is.True, manifestPhysicalPath);

            Assert.That(
                VividMeshShaderProgramImporter.TryResolvePhysicalAssetPath(
                    CanonicalSourcePath,
                    out string sourcePhysicalPath),
                Is.True);
            Assert.That(File.Exists(sourcePhysicalPath), Is.True, sourcePhysicalPath);

            string expectedSourceAssetPath =
                $"{packageInfo.assetPath}/Shaders/Core/Private/GPUDriven/VisibilityBufferMeshShader.hlsl";
            Assert.That(
                VividMeshShaderProgramImporter.GetAssetDatabasePath(CanonicalSourcePath),
                Is.EqualTo(expectedSourceAssetPath));
        }

        [Test]
        public void Manifest_ReferencesNormalHlslAndCurrentRootLayout()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(
                typeof(VividMeshShaderProgramAsset).Assembly);
            Assert.That(packageInfo, Is.Not.Null);

            string manifestPath = Path.Combine(
                packageInfo.resolvedPath,
                "Runtime",
                "Resources",
                "VividMeshShader",
                "VisibilityBufferMeshShader.vms");
            var manifest = JsonUtility.FromJson<VividMeshShaderProgramImporter.Manifest>(
                File.ReadAllText(manifestPath));

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.source, Is.EqualTo(CanonicalSourcePath));
            Assert.That(manifest.source, Does.EndWith(".hlsl"));
            Assert.That(manifest.source, Does.Not.EndWith(".hlsl.txt"));
            Assert.That(
                manifest.rootLayoutVersion,
                Is.EqualTo(VividMeshShaderProgramAsset.CurrentRootLayoutVersion));
            Assert.That(manifest.amplificationProfile, Is.EqualTo("as_6_5"));
            Assert.That(manifest.meshProfile, Is.EqualTo("ms_6_5"));
            Assert.That(manifest.pixelProfile, Is.EqualTo("ps_6_5"));
        }

        [Test]
        public void ImportedProgram_ContainsAllDxilStages()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(
                typeof(VividMeshShaderProgramAsset).Assembly);
            Assert.That(packageInfo, Is.Not.Null);

            string programAssetPath =
                $"{packageInfo.assetPath}/Runtime/Resources/VividMeshShader/VisibilityBufferMeshShader.vms";
            VividMeshShaderProgramAsset program =
                UnityEditor.AssetDatabase.LoadAssetAtPath<VividMeshShaderProgramAsset>(programAssetPath);

            Assert.That(program, Is.Not.Null, programAssetPath);
            Assert.That(program.AmplificationDxil.Length, Is.GreaterThan(0));
            Assert.That(program.MeshDxil.Length, Is.GreaterThan(0));
            Assert.That(program.PixelDxil.Length, Is.GreaterThan(0));
            Assert.That(program.CompilerAbiVersion, Is.EqualTo(VividMeshShaderCompiler.AbiVersion));
            Assert.That(
                program.RootLayoutVersion,
                Is.EqualTo(VividMeshShaderProgramAsset.CurrentRootLayoutVersion));
            StringAssert.StartsWith("DXC ", program.CompilerVersion);
        }
    }
}
