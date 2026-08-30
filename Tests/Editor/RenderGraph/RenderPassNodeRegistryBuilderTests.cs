using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Experimental.Material;

namespace VividRP.Editor.Tests
{
    public class RenderPassNodeRegistryIntegrationTests
    {
        [Test]
        public void GeneratedRegistry_MapsKnownPassInBothDirections()
        {
            var nodeType = RenderPassNodeRegistry.GetNodeType(typeof(FullScreenPass));

            Assert.That(nodeType, Is.Not.Null);
            Assert.That(nodeType.Name, Is.EqualTo(nameof(FullScreenPass)));
            Assert.That(nodeType.Namespace, Is.EqualTo("VividRP.Editor.RenderGraph.Generated"));
            Assert.That(typeof(RenderPassNodeData).IsAssignableFrom(nodeType), Is.True);
            Assert.That(RenderPassNodeRegistry.GetPassType(nodeType), Is.EqualTo(typeof(FullScreenPass)));
        }

        [Test]
        public void GeneratedRegistry_ExcludesAbstractPassTypes()
        {
            Assert.That(RenderPassNodeRegistry.GetNodeType(typeof(RasterPass)), Is.Null);
        }

        [Test]
        public void GeneratedRegistry_ExcludesObsoletePassTypes()
        {
#pragma warning disable CS0618
            var nodeType = RenderPassNodeRegistry.GetNodeType(typeof(ExperimentalVisibilityBufferPass));
#pragma warning restore CS0618

            Assert.That(nodeType, Is.Null);
        }

        [Test]
        public void GeneratedRegistry_AllGeneratedNodesRoundTrip()
        {
            var generatedNodeTypes = TypeCache.GetTypesDerivedFrom<RenderPassNodeData>()
                .Where(type => type.Namespace == "VividRP.Editor.RenderGraph.Generated")
                .ToArray();

            Assert.That(generatedNodeTypes, Is.Not.Empty);
            foreach (var nodeType in generatedNodeTypes)
            {
                var passType = RenderPassNodeRegistry.GetPassType(nodeType);
                Assert.That(passType, Is.Not.Null, nodeType.FullName);
                Assert.That(RenderPassNodeRegistry.GetNodeType(passType), Is.EqualTo(nodeType), nodeType.FullName);
            }
        }

        [Test]
        public void GeneratedRegistry_CoversEveryEligibleRuntimePass()
        {
            var runtimeAssembly = typeof(IRenderPass).Assembly;
            var eligiblePassTypes = TypeCache.GetTypesDerivedFrom<IRenderPass>()
                .Where(type =>
                    type.Assembly == runtimeAssembly
                    && type.IsClass
                    && !type.IsAbstract
                    && !type.ContainsGenericParameters
                    && type.IsVisible
                    && type.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Length == 0
                    && type.GetConstructor(Type.EmptyTypes) != null)
                .ToArray();

            Assert.That(eligiblePassTypes, Is.Not.Empty);
            foreach (var passType in eligiblePassTypes)
            {
                var nodeType = RenderPassNodeRegistry.GetNodeType(passType);
                Assert.That(nodeType, Is.Not.Null, passType.FullName);
                Assert.That(RenderPassNodeRegistry.GetPassType(nodeType), Is.EqualTo(passType), passType.FullName);
            }
        }
    }
}
