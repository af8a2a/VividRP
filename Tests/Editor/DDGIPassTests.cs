using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class DDGIPassTests
    {
        [Test]
        public void DDGIPasses_UseImportedPersistentResourcesAndSingleVolumeBindings()
        {
            var rtasBuildSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DDGIRTASBuildPass.cs"));
            var traceSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DDGIProbeTracePass.cs"));
            var blendSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "DDGIProbeBlendPass.cs"));

            Assert.That(rtasBuildSource, Does.Contain("RenderGraphAccelerationStructureDesc.Create(\"DDGIRTAS\")"));
            Assert.That(rtasBuildSource, Does.Contain("DDGISystem.instance.AccelerationStructure"));
            Assert.That(traceSource, Does.Contain("CreateImportedTexture(\"DDGIProbeRayData\""));
            Assert.That(traceSource, Does.Contain("CreateImportedBuffer(\"DDGIInstances\""));
            Assert.That(traceSource, Does.Contain("DDGISystem.instance.ProbeVariabilityHandle != null"));
            Assert.That(traceSource, Does.Contain("DDGISystem.instance.AccelerationStructure != null"));
            Assert.That(traceSource, Does.Contain("nativeCmd.SetRayTracingAccelerationStructure("));
            Assert.That(traceSource, Does.Contain("ConstantBuffer.Push(nativeCmd, m_RootConstants"));
            Assert.That(traceSource, Does.Contain("m_ClearWidth"));
            Assert.That(blendSource, Does.Contain("DDGIProbeBlendIrradianceCompute"));
            Assert.That(blendSource, Does.Contain("DDGIProbeBlendDistanceCompute"));
            Assert.That(blendSource, Does.Contain("DDGISystem.instance.ProbeVariabilityHandle != null"));
            Assert.That(blendSource, Does.Contain("DispatchIrradianceBlend(cmd, nativeCmd);"));
            Assert.That(blendSource, Does.Contain("DispatchDistanceBlend(cmd, nativeCmd);"));
        }

        [Test]
        public void DDGIRuntime_WiresSystemUpdateAndGlobalQueryStateIntoPipeline()
        {
            var pipelineSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPipeline", "VividRenderPipeline.cs"));
            var recorderSource = File.ReadAllText(GetPackageFilePath("Runtime", "RenderGraph", "PassRecorder.Execution.cs"));

            Assert.That(pipelineSource, Does.Contain("DDGISystem.instance.Update(PassRecorder.GetFrameData());"));
            Assert.That(pipelineSource, Does.Contain("DDGISystem.instance.BindGlobalQueryState(cmdBuffer);"));
            Assert.That(pipelineSource, Does.Contain("DDGISystem.Shutdown();"));
            Assert.That(recorderSource, Does.Contain("internal static ContextContainer GetFrameData()"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (string packageRoot in packageRoots)
            {
                string fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
