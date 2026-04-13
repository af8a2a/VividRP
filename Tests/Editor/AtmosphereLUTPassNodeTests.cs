using System;
using System.IO;
using NUnit.Framework;
using VividRP.Editor.RenderGraph;
using RuntimeAtmosphereLUTPass = VividRP.Runtime.RenderPass.Core.AtmosphereLUTPass;

#pragma warning disable CS0618
namespace VividRP.Editor.Tests
{
    public class AtmosphereLUTPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredAtmosphereLUTPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(RuntimeAtmosphereLUTPass).AssemblyQualifiedName;
        }

        [Test]
        public void AtmosphereLUTPassNode_DefinesLutOutputPorts()
        {
            var node = new AutoRegisteredAtmosphereLUTPassNode();

            Assert.That(node.GetOutputPortByName("m_AtmosphericScatteringLUT"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_MultiScatteringLUT"), Is.Not.Null);
            Assert.That(node.GetOutputPortByName("m_SkyViewLUT"), Is.Not.Null);
        }

        [Test]
        public void GeneratedNodeRegistry_RegistersAtmosphereLUTPass()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class AtmosphereLUTPass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.AtmosphereLUTPass, VividRP.Runtime"));
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
#pragma warning restore CS0618
