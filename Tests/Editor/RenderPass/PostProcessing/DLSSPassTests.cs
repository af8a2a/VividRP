using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class DLSSPassTests
    {
        [Test]
        public void SourceFiles_ExposeDlssCameraModeAndInjection()
        {
            var cameraDataSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "ComponentData",
                "VividAdditionalCameraData.cs"));
            var passRecorderSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderGraph",
                "PassRecorder.Execution.cs"));
            var dlssPassSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "RenderPass",
                "Core",
                "PostProcessing",
                "DLSS",
                "DLSSPass.cs"));

            Assert.That(cameraDataSource, Does.Contain("DeepLearningSuperSampling"));
            Assert.That(cameraDataSource, Does.Contain("DLSSQuality m_DLSSQuality"));
            Assert.That(passRecorderSource, Does.Contain("if (DLSSExtension.IsSuperResolutionSupported)"));
            Assert.That(passRecorderSource, Does.Contain("private static bool ShouldInjectDlssPass()"));
            Assert.That(passRecorderSource, Does.Contain("RecordInjectedDlssPass("));
            Assert.That(dlssPassSource, Does.Contain("DLSSSuperResolution"));
            Assert.That(dlssPassSource, Does.Contain("builder.AllowGlobalStateModification(true)"));
        }

        [Test]
        public void SourceFiles_IncludeNativeBindingsAndResources()
        {
            var dlssExtensionSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "SubSystem",
                "DLSS",
                "DLSSExtension.cs"));
            var resourcesSource = File.ReadAllText(GetPackageFilePath(
                "Runtime",
                "Utility",
                "PipelineResource",
                "VividResources.cs"));

            Assert.That(dlssExtensionSource, Does.Contain("UnityDLSS"));
            Assert.That(dlssExtensionSource, Does.Contain("DLSS_Init_with_ProjectID_D3D12"));
            Assert.That(resourcesSource, Does.Contain("DLSSBiasColorMaskShader"));
            Assert.That(resourcesSource, Does.Contain("DLSSRRResourcePrepCompute"));
            Assert.That(File.Exists(GetPackageFilePath("Runtime", "SubSystem", "DLSS", "UnityDLSS.dll")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Runtime", "SubSystem", "DLSS", "nvngx_dlss.dll")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Private", "DLSS", "DLSSRRResourcePrep.compute")), Is.True);
            Assert.That(File.Exists(GetPackageFilePath("Shaders", "Core", "Public", "Raytracing", "RayTracingGBufferOutput.hlsl")), Is.True);
        }

        private static string GetPackageFilePath(params string[] parts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(parts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(parts));
        }
    }
}
