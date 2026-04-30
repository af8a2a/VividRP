using System;
using System.IO;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class AtmosphericScatteringPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredAtmosphericScatteringPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(AtmosphericScatteringPass);
        }

        [Test]
        public void AtmosphericScatteringPassNode_DefinesExpectedInputAndOutputPorts()
        {
            var node = new AutoRegisteredAtmosphericScatteringPassNode();

            Assert.That(node.GetInputPortByName("m_ColorInput"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_VBufferLighting"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_AtmosphericScatteringLUT"), Is.Null);
            Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
        }

        [Test]
        public void GeneratedNodeRegistry_RegistersAtmosphericScatteringPass()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class AtmosphericScatteringPass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.AtmosphericScatteringPass, VividRP.Runtime"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var packageRoots = new[]
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
