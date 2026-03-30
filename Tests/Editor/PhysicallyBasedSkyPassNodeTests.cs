using System;
using System.IO;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using RuntimePhysicallyBasedSkyPass = VividRP.Runtime.RenderPass.Core.PhysicallyBasedSkyPass;

namespace VividRP.Editor.Tests
{
    public class PhysicallyBasedSkyPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredPhysicallyBasedSkyPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RuntimePhysicallyBasedSkyPass).AssemblyQualifiedName;
        }

        [Test]
        public void PhysicallyBasedSkyPassNode_DefinesDepthInputAndColorOutputPorts()
        {
            var node = new AutoRegisteredPhysicallyBasedSkyPassNode();

            Assert.That(node.GetInputPortByName("m_DepthTexture"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Not.Null);
        }

        [Test]
        public void GeneratedNodeRegistry_RegistersPhysicallyBasedSkyPass()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class PhysicallyBasedSkyPass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.PhysicallyBasedSkyPass, VividRP.Runtime"));
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
