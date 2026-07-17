using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.TestTools;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class RenderGraphDrawObjectPassMigrationTests
    {
        [Test]
        public void Migrate_CopiesDirectResourceDescriptorAndRemovesUnusedResource()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var resourceNode = AddRenderListResource(graph, CreateTransparentDescriptor());
                var drawNode = AddDrawObjectNode(graph);
                var sourceShaderTagNames = resourceNode.GetDescriptor().ShaderTagNames;
                Assert.That(graph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(drawNode)), Is.True);

                var changed = RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");
                var embeddedDescriptor = GetEmbeddedDescriptor(drawNode);

                Assert.That(changed, Is.True);
                Assert.That(graph.SchemaVersion, Is.EqualTo(RenderGraphEditorGraph.CurrentSchemaVersion));
                Assert.That(drawNode.GetInputPortByName(MigrationDrawObjectPass.RenderListFieldName), Is.Null);
                Assert.That(graph.GetNodes().Contains(resourceNode), Is.False);
                Assert.That(embeddedDescriptor.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Transparent));
                Assert.That(embeddedDescriptor.ShaderTagNames, Is.EqualTo(new[] { "TransparentCharacter", "SpecialForward" }));
                Assert.That(embeddedDescriptor.ShaderTagNames, Is.Not.SameAs(sourceShaderTagNames));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_RemovesSharedResource_AfterAllDrawObjectConsumersAreEmbedded()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var resourceNode = AddRenderListResource(graph, CreateTransparentDescriptor());
                var firstDrawNode = AddDrawObjectNode(graph);
                var secondDrawNode = AddDrawObjectNode(graph);
                Assert.That(graph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(firstDrawNode)), Is.True);
                Assert.That(graph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(secondDrawNode)), Is.True);

                RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");

                Assert.That(graph.GetNodes().Contains(resourceNode), Is.False);
                Assert.That(GetEmbeddedDescriptor(firstDrawNode).ShaderTagNames, Is.EqualTo(new[] { "TransparentCharacter", "SpecialForward" }));
                Assert.That(GetEmbeddedDescriptor(secondDrawNode).ShaderTagNames, Is.EqualTo(new[] { "TransparentCharacter", "SpecialForward" }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_KeepsResource_WhenAnotherPassStillConsumesIt()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var resourceNode = AddRenderListResource(graph, CreateTransparentDescriptor());
                var drawNode = AddDrawObjectNode(graph);
                var consumerNode = new MigrationRenderListConsumerNode();
                RenderGraphTestUtility.AddTestNode(graph, consumerNode);
                Assert.That(graph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(drawNode)), Is.True);
                Assert.That(
                    graph.Connect(
                        GetResourceOutput(resourceNode),
                        consumerNode.GetInputPortByName(MigrationRenderListConsumerPass.RenderListFieldName)),
                    Is.True);

                RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");

                Assert.That(graph.GetNodes().Contains(resourceNode), Is.True);
                Assert.That(GetResourceOutput(resourceNode).IsConnected, Is.True);
                Assert.That(drawNode.GetInputPortByName(MigrationDrawObjectPass.RenderListFieldName), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_EnablesOverrideAndPreservesDynamicPassConnection()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var producerNode = new MigrationRenderListProducerNode();
                var drawNode = AddDrawObjectNode(graph);
                RenderGraphTestUtility.AddTestNode(graph, producerNode);
                Assert.That(
                    graph.Connect(
                        producerNode.GetOutputPortByName(MigrationRenderListProducerPass.RenderListFieldName),
                        GetRenderListInput(drawNode)),
                    Is.True);

                RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");

                var renderListInput = drawNode.GetInputPortByName(MigrationDrawObjectPass.RenderListFieldName);
                Assert.That(renderListInput, Is.Not.Null);
                Assert.That(renderListInput.IsConnected, Is.True);
                Assert.That(GetOverrideValue(drawNode), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_UpdatesRootAndSubSystemGraphs_AndIsIdempotent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var subgraphNode = RenderGraphSubSystemTestUtility.CreateSubSystem(graph, out var subSystemGraph);
                Assert.That(subgraphNode, Is.Not.Null);
                var resourceNode = AddRenderListResource(subSystemGraph, CreateTransparentDescriptor());
                var drawNode = AddDrawObjectNode(subSystemGraph);
                Assert.That(subSystemGraph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(drawNode)), Is.True);

                var firstChanged = RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");
                var secondChanged = RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");

                Assert.That(firstChanged, Is.True);
                Assert.That(secondChanged, Is.False);
                Assert.That(graph.SchemaVersion, Is.EqualTo(RenderGraphEditorGraph.CurrentSchemaVersion));
                Assert.That(subSystemGraph.SchemaVersion, Is.EqualTo(RenderGraphEditorGraph.CurrentSchemaVersion));
                Assert.That(subSystemGraph.GetNodes().Contains(resourceNode), Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_DoesNotChangeCurrentSchemaExplicitOverride()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                graph.SchemaVersion = RenderGraphEditorGraph.CurrentSchemaVersion;
                var resourceNode = AddRenderListResource(graph, CreateTransparentDescriptor());
                var drawNode = AddDrawObjectNode(graph);
                Assert.That(graph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(drawNode)), Is.True);

                var changed = RenderGraphDrawObjectPassMigration.Migrate(graph, "Assets/Test.vrdg");

                Assert.That(changed, Is.False);
                Assert.That(graph.GetNodes().Contains(resourceNode), Is.True);
                Assert.That(GetRenderListInput(drawNode).IsConnected, Is.True);
                Assert.That(GetOverrideValue(drawNode), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [UnityTest]
        public IEnumerator Importer_PersistsLegacyDirectResourceMigration_AndReimportIsIdempotent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            var assetPath = GraphDatabase.GetGraphAssetPath(graph);

            try
            {
                var resourceNode = AddRenderListResource(graph, CreateTransparentDescriptor());
                var drawNode = AddDrawObjectNode(graph);
                Assert.That(graph.Connect(GetResourceOutput(resourceNode), GetRenderListInput(drawNode)), Is.True);
                graph.SchemaVersion = 0;
                GraphDatabase.SaveGraph(graph);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                var runtimeAsset = AssetDatabase.LoadAssetAtPath<RenderGraphData>(assetPath);
                Assert.That(runtimeAsset, Is.Not.Null);
                var runtimePass = runtimeAsset.Passes.Single(
                    pass => pass.PassType.StartsWith(typeof(MigrationDrawObjectPass).FullName));
                Assert.That(runtimeAsset.RenderListDescriptors, Is.Empty);
                Assert.That(runtimePass.ResourceBindings.Any(binding => binding.FieldName == "m_RenderList"), Is.False);
                Assert.That(runtimePass.RenderListDescParameters, Has.Count.EqualTo(1));
                Assert.That(
                    runtimePass.RenderListDescParameters[0].Value.RenderQueueRange,
                    Is.EqualTo(RenderGraphRenderQueueRange.Transparent));

                yield return null;
                yield return null;

                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var persistedGraph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(assetPath);
                Assert.That(persistedGraph, Is.Not.Null);
                Assert.That(persistedGraph.SchemaVersion, Is.EqualTo(RenderGraphEditorGraph.CurrentSchemaVersion));
                Assert.That(persistedGraph.GetNodes().OfType<RenderListResourceNodeData>(), Is.Empty);
                Assert.That(RenderGraphDrawObjectPassMigration.Migrate(persistedGraph, assetPath), Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static RenderListResourceNodeData AddRenderListResource(
            RenderGraphEditorGraph graph,
            RenderGraphRenderListDesc descriptor)
        {
            var resourceNode = new RenderListResourceNodeData();
            RenderGraphTestUtility.AddTestNode(graph, resourceNode);
            var descriptorOption = resourceNode.GetNodeOptionByName("Descriptor");
            Assert.That(descriptorOption, Is.Not.Null);
            Assert.That(descriptorOption.TrySetValue(descriptor), Is.True);
            return resourceNode;
        }

        private static MigrationDrawObjectNode AddDrawObjectNode(RenderGraphEditorGraph graph)
        {
            var drawNode = new MigrationDrawObjectNode();
            RenderGraphTestUtility.AddTestNode(graph, drawNode);
            var overrideOption = drawNode.GetNodeOptionByName(
                RenderPassPortUtility.GetOverrideOptionName(MigrationDrawObjectPass.RenderListFieldName));
            Assert.That(overrideOption, Is.Not.Null);
            Assert.That(overrideOption.TrySetValue(true), Is.True);
            drawNode.DefineNode();
            return drawNode;
        }

        private static IPort GetResourceOutput(RenderListResourceNodeData resourceNode)
        {
            return resourceNode.GetOutputPortByName(RenderListResourceNodeData.OutputPortName);
        }

        private static IPort GetRenderListInput(MigrationDrawObjectNode drawNode)
        {
            return drawNode.GetInputPortByName(MigrationDrawObjectPass.RenderListFieldName);
        }

        private static RenderGraphRenderListDesc GetEmbeddedDescriptor(MigrationDrawObjectNode drawNode)
        {
            var option = drawNode.GetNodeOptionByName(
                RenderGraphPassRenderListDescParameterUtility.GetOptionName(MigrationDrawObjectPass.RenderListDescFieldName));
            Assert.That(option, Is.Not.Null);
            Assert.That(option.TryGetValue<RenderGraphRenderListDesc>(out var descriptor), Is.True);
            return descriptor;
        }

        private static bool GetOverrideValue(MigrationDrawObjectNode drawNode)
        {
            var option = drawNode.GetNodeOptionByName(
                RenderPassPortUtility.GetOverrideOptionName(MigrationDrawObjectPass.RenderListFieldName));
            Assert.That(option, Is.Not.Null);
            Assert.That(option.TryGetValue<bool>(out var enabled), Is.True);
            return enabled;
        }

        private static RenderGraphRenderListDesc CreateTransparentDescriptor()
        {
            return RenderGraphRenderListDesc.CreateTransparent("TransparentCharacter", "SpecialForward");
        }
    }

    internal sealed class MigrationDrawObjectPass : DrawObjectPass, IRenderGraphSideEffectPass
    {
        internal const string RenderListFieldName = "m_RenderList";
        internal const string RenderListDescFieldName = "m_RenderListDesc";
    }

    [Serializable]
    internal sealed class MigrationDrawObjectNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(MigrationDrawObjectPass);
    }

    internal sealed class MigrationRenderListProducerPass : RasterPass
    {
        internal const string RenderListFieldName = "m_RenderList";

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Write)]
        private RenderGraphRenderList m_RenderList = new();

        public override void Create() { }
        public override void Prepare(ContextContainer frameData) { }
        public override void Record(RasterPassContext context) { }
        public override void Dispose() { }
    }

    [Serializable]
    internal sealed class MigrationRenderListProducerNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(MigrationRenderListProducerPass);
    }

    internal sealed class MigrationRenderListConsumerPass : RasterPass
    {
        internal const string RenderListFieldName = "m_RenderList";

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList = new();

        public override void Create() { }
        public override void Prepare(ContextContainer frameData) { }
        public override void Record(RasterPassContext context) { }
        public override void Dispose() { }
    }

    [Serializable]
    internal sealed class MigrationRenderListConsumerNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(MigrationRenderListConsumerPass);
    }
}
