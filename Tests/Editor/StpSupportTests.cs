using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class StpSupportTests
    {
        [Test]
        public void PipelineAsset_ImplementsStpEnabledInterface()
        {
            var asset = ScriptableObject.CreateInstance<VividRenderPipelineAsset>();

            try
            {
                var stpAsset = asset as ISTPEnabledRenderPipeline;

                Assert.That(stpAsset, Is.Not.Null);
                Assert.That(stpAsset.isStpUsed, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void SourceFiles_RegisterAndInjectStp()
        {
            var globalSettingsSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipelineGlobalSettings.cs"));
            var passRecorderSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(globalSettingsSource, Does.Contain("UnityEngine.Rendering.STP+RuntimeResources"));
            Assert.That(passRecorderSource, Does.Contain("private static bool ShouldInjectStpPass()"));
            Assert.That(passRecorderSource, Does.Contain("STP.Execute(renderGraph, ref config)"));
            Assert.That(passRecorderSource, Does.Contain("STP.Jit16(Time.frameCount)"));
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
