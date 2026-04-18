using System;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class RenderPassNodeNavigationUtilityTests
    {
        [Serializable]
        private sealed class AutoRegisteredFullScreenPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(FullScreenPass);
        }

        [Serializable]
        private sealed class MissingPassNode : RenderPassNodeData
        {
        }

        [Test]
        public void TryGetPassScript_ReturnsMonoScript_WhenPassNodeUsesRegisteredPassType()
        {
            var node = new AutoRegisteredFullScreenPassNode();

            var result = node.TryGetPassScript(out var script);

            Assert.That(result, Is.True);
            Assert.That(script, Is.Not.Null);
            Assert.That(script.GetClass(), Is.EqualTo(typeof(FullScreenPass)));
        }

        [Test]
        public void TryGetPassScript_ReturnsFalse_WhenPassTypeCannotBeResolved()
        {
            var node = new MissingPassNode();

            var result = node.TryGetPassScript(out var script);

            Assert.That(result, Is.False);
            Assert.That(script, Is.Null);
        }

        [Test]
        public void TryGetRenderPassNode_ReturnsBackingNode_WhenNodeModelBelongsToRenderPassNode()
        {
            var node = new AutoRegisteredFullScreenPassNode();
            var nodeModel = GetNodeModel(node);

            var result = RenderPassNodeNavigationUtility.TryGetRenderPassNode(nodeModel, out var resolvedNode);

            Assert.That(result, Is.True);
            Assert.That(resolvedNode, Is.SameAs(node));
        }

        [Test]
        public void TryGetRenderPassNode_ReturnsFalse_WhenNodeModelBelongsToResourceNode()
        {
            var node = new TextureResourceNodeData();
            var nodeModel = GetNodeModel(node);

            var result = RenderPassNodeNavigationUtility.TryGetRenderPassNode(nodeModel, out var resolvedNode);

            Assert.That(result, Is.False);
            Assert.That(resolvedNode, Is.Null);
        }

        private static object GetNodeModel(Node node)
        {
            var method = typeof(Node).GetMethod("GetImplementation", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var nodeModel = method.Invoke(node, null);
            Assert.That(nodeModel, Is.Not.Null);
            return nodeModel;
        }
    }
}
