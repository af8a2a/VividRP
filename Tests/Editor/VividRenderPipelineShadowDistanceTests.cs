using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class VividRenderPipelineShadowDistanceTests
    {
        [Test]
        public void VividRenderPipeline_ClampsCullingShadowDistanceToActiveCSMVolumeBeforeCull()
        {
            var source = File.ReadAllText(GetPipelineSourcePath());

            Assert.That(source, Does.Contain("VividVolumeManagerUtility.Update(camera);"));
            Assert.That(source, Does.Contain("ApplyShadowDistanceOverride(camera, ref cullingParameters);"));
            Assert.That(source, Does.Contain("var shadowDistance = Mathf.Min(cullingParameters.shadowDistance, camera.farClipPlane);"));
            Assert.That(source, Does.Contain("var csmSettings = VividVolumeManagerUtility.GetCascadedShadowSettingsVolume();"));
            Assert.That(source, Does.Contain("if (csmSettings != null && csmSettings.IsActive())"));
            Assert.That(source, Does.Contain("shadowDistance = Mathf.Min(shadowDistance, csmSettings.maxShadowDistance.value);"));
            Assert.That(source, Does.Contain("cullingParameters.shadowDistance = Mathf.Max(0.0f, shadowDistance);"));
        }

        private static string GetPipelineSourcePath()
        {
            var pipelinePath = GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipeline.cs");

            Assert.That(File.Exists(pipelinePath), Is.True, $"Expected render pipeline source at '{pipelinePath}'.");
            return pipelinePath;
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
