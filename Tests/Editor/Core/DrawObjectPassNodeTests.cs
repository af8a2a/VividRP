using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using VividRP.Runtime;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class DrawObjectPassNodeTests
    {
        private sealed class DerivedDrawObjectPass : DrawObjectPass
        {
        }

        [Serializable]
        private sealed class AutoRegisteredDrawObjectPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DrawObjectPass);
        }

        [Serializable]
        private sealed class AutoRegisteredDerivedDrawObjectPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DerivedDrawObjectPass);
        }

        [Test]
        public void DrawObjectPassNode_UsesEmbeddedRenderListDescriptorByDefault()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                var renderListInput = node.GetInputPortByName("m_RenderList");
                var descriptorOption = node.GetNodeOptionByName(
                    RenderGraphPassRenderListDescParameterUtility.GetOptionName("m_RenderListDesc"));

                Assert.That(renderListInput, Is.Null);
                Assert.That(descriptorOption, Is.Not.Null);
                Assert.That(descriptorOption.TryGetValue<RenderGraphRenderListDesc>(out var descriptor), Is.True);
                Assert.That(descriptor, Is.Not.Null);
                Assert.That(descriptor.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Opaque));
                Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Null);
                Assert.That(node.GetInputPortByName("m_DepthTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DepthTarget_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DrawObjectPassNode_DefinesRenderListInput_WhenOverrideIsEnabled()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                var overrideOption = node.GetNodeOptionByName(RenderPassPortUtility.GetOverrideOptionName("m_RenderList"));
                Assert.That(overrideOption, Is.Not.Null);
                Assert.That(overrideOption.TrySetValue(true), Is.True);
                node.DefineNode();

                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DerivedDrawObjectPassNode_InheritsEmbeddedRenderListAndAttachmentLayout()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDerivedDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Null);
                Assert.That(
                    node.GetNodeOptionByName(RenderGraphPassRenderListDescParameterUtility.GetOptionName("m_RenderListDesc")),
                    Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ColorTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget_Out"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ColorTarget"), Is.Null);
                Assert.That(node.GetInputPortByName("m_DepthTarget_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_DepthTarget_Out"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DrawObjectPassNode_Rename_UpdatesAuthoredPassTitle()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                var renamed = RenderPassNodeRenameUtility.Rename(node, "Transparent Characters");

                Assert.That(renamed, Is.True);
                Assert.That(node.Title, Is.EqualTo("Transparent Characters"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DrawObjectPassNode_Rename_ResetsBlankTitleToPassTypeName()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var node = new AutoRegisteredDrawObjectPassNode();
            RenderGraphTestUtility.AddTestNode(graph, node);

            try
            {
                RenderPassNodeRenameUtility.Rename(node, "Transparent Characters");

                var renamed = RenderPassNodeRenameUtility.Rename(node, "   ");

                Assert.That(renamed, Is.True);
                Assert.That(node.Title, Is.EqualTo(nameof(DrawObjectPass)));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DrawObjectPassNode_Rename_PersistsAndCompilesAfterFreshLoad()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var assetPath = GraphDatabase.GetGraphAssetPath(graph);
            var node = new VividRP.Editor.RenderGraph.Generated.DrawObjectPass();
            var finalBlitNode = new VividRP.Editor.RenderGraph.Generated.FinalBlitPass();
            RenderGraphTestUtility.AddTestNode(graph, node);
            RenderGraphTestUtility.AddTestNode(graph, finalBlitNode);

            try
            {
                RenderPassNodeRenameUtility.Rename(node, "DrawTransparentPass");
                graph.Connect(
                    node.GetOutputPortByName("m_ColorTarget_Out"),
                    finalBlitNode.GetInputPortByName("source"));
                GraphDatabase.SaveGraph(graph);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                var runtimeAsset = AssetDatabase.LoadAssetAtPath<RenderGraphData>(assetPath);
                Assert.That(runtimeAsset, Is.Not.Null);
                var importedPass = runtimeAsset.Passes.Single(pass =>
                    pass.PassType.StartsWith(typeof(DrawObjectPass).FullName, StringComparison.Ordinal));
                var freshGraph = GraphDatabase.LoadGraphForImporter<RenderGraphEditorGraph>(assetPath);
                Assert.That(freshGraph, Is.Not.Null);
                var freshNode = freshGraph.GetNodes()
                    .OfType<VividRP.Editor.RenderGraph.Generated.DrawObjectPass>()
                    .Single();
                var compiledPass = RenderGraphCompiler.Compile(freshGraph).Passes.Single(pass =>
                    pass.PassType.StartsWith(typeof(DrawObjectPass).FullName, StringComparison.Ordinal));

                Assert.That(freshNode.Title, Is.EqualTo("DrawTransparentPass"));
                Assert.That(freshNode.GetAuthoredPassName(null), Is.EqualTo("DrawTransparentPass"));
                Assert.That(importedPass.PassName, Is.EqualTo("DrawTransparentPass"));
                Assert.That(compiledPass.PassName, Is.EqualTo("DrawTransparentPass"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }
}
