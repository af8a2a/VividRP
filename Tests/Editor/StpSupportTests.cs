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
        public void SourceFiles_RegisterAndRecordStpThroughExplicitAntialiasingPass()
        {
            var globalSettingsSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipelineGlobalSettings.cs"));
            var passRecorderSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));
            var antialiasingPassSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderPass", "Core", "AntialiasingPass.cs"));
            var resolverSource = File.ReadAllText(
                GetPackageFilePath("Runtime", "RenderGraph", "FrameContext", "VividAntialiasingData.cs"));

            Assert.That(globalSettingsSource, Does.Contain("UnityEngine.Rendering.STP+RuntimeResources"));
            Assert.That(passRecorderSource, Does.Not.Contain("ShouldInjectStpPass"));
            Assert.That(passRecorderSource, Does.Not.Contain("RecordInjectedStpPass"));
            Assert.That(antialiasingPassSource, Does.Contain("TryRecordStpPass"));
            Assert.That(antialiasingPassSource, Does.Contain("STP.Execute(context.RenderGraph, ref config)"));
            Assert.That(resolverSource, Does.Contain("STP.Jit16(ResolveTemporalFrameIndex(frameIndex))"));
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
