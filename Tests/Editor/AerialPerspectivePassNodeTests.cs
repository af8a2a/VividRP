using System;
using System.IO;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using RuntimeAerialPerspectivePass = VividRP.Runtime.RenderPass.Core.AerialPerspectivePass;

namespace VividRP.Editor.Tests
{
    public class AerialPerspectivePassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredAerialPerspectivePassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RuntimeAerialPerspectivePass).AssemblyQualifiedName;
        }

        [Test]
        public void AerialPerspectivePassNode_DefinesExpectedInputAndOutputPorts()
        {
            var node = new AutoRegisteredAerialPerspectivePassNode();

            Assert.That(node.GetInputPortByName("m_ColorInput"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
            Assert.That(node.GetInputPortByName("m_AtmosphericScatteringLUT"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_OutputTexture"), Is.Not.Null);
        }

        [Test]
        public void GeneratedNodeRegistry_RegistersAerialPerspectivePass()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class AerialPerspectivePass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.AerialPerspectivePass, VividRP.Runtime"));
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
