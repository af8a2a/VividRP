using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using static VividRP.Editor.Tests.RenderGraphSubSystemTestUtility;

namespace VividRP.Editor.Tests
{
    public class RenderGraphSubSystemGraphTests
    {
        [Test]
        public void GraphAttributes_ReserveAssetExtension_ForRootGraphOnly()
        {
            var rootGraphAttribute = typeof(RenderGraphEditorGraph).GetCustomAttribute<GraphAttribute>();
            var subSystemGraphAttribute = typeof(RenderGraphSubSystemGraph).GetCustomAttribute<GraphAttribute>();

            Assert.That(rootGraphAttribute, Is.Not.Null);
            Assert.That(subSystemGraphAttribute, Is.Not.Null);
            Assert.That((rootGraphAttribute.Options & GraphOptions.SupportsSubgraphs) != 0, Is.True);
            Assert.That((subSystemGraphAttribute.Options & GraphOptions.SupportsSubgraphs) != 0, Is.False);
            Assert.That(subSystemGraphAttribute.Extension, Is.Not.EqualTo(rootGraphAttribute.Extension));
        }
    }

    public class RenderGraphSubSystemCompilerTests
    {
        [Test]
        public void Compile_ExpandsSubSystemPassDependencies_WhenPassFlowsAcrossBoundary()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var producerNode = new TextureProducerPassNode();
                var consumerNode = new TextureConsumerPassNode();
                RenderGraphTestUtility.AddTestNode(graph, producerNode);
                RenderGraphTestUtility.AddTestNode(graph, consumerNode);

                var subgraphNode = CreateSubSystem(graph, out var subSystemGraph);
                var inputVariable = subSystemGraph.CreateVariable(
                    "InputTexture",
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Input);
                var outputVariable = subSystemGraph.CreateVariable(
                    "OutputTexture",
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Output);

                var inputVariableNode = subSystemGraph.AddVariableNode(inputVariable, new Vector2(50f, 50f));
                var outputVariableNode = subSystemGraph.AddVariableNode(outputVariable, new Vector2(350f, 50f));
                var innerPassNode = new TexturePassthroughPassNode();
                RenderGraphTestUtility.AddTestNode(subSystemGraph, innerPassNode);

                Assert.That(graph.Connect(
                    producerNode.GetOutputPortByName(TextureProducerPass.OutputFieldName),
                    GetRequiredInputPort(subgraphNode, inputVariable)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    GetRequiredOutputPort(inputVariableNode),
                    innerPassNode.GetInputPortByName(TexturePassthroughPass.InputFieldName)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    innerPassNode.GetOutputPortByName(TexturePassthroughPass.OutputFieldName),
                    GetRequiredInputPort(outputVariableNode)),
                    Is.True);
                Assert.That(graph.Connect(
                    GetRequiredOutputPort(subgraphNode, outputVariable),
                    consumerNode.GetInputPortByName(TextureConsumerPass.InputFieldName)),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(TextureProducerPass),
                    nameof(TexturePassthroughPass),
                    nameof(TextureConsumerPass),
                }));
                Assert.That(result.Passes, Has.Count.EqualTo(3));
                Assert.That(result.Passes[1].ResourceBindings, Has.Count.EqualTo(1));
                Assert.That(result.Passes[1].ResourceBindings[0].SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
                Assert.That(result.Passes[1].ResourceBindings[0].SourcePassIndex, Is.EqualTo(0));
                Assert.That(result.Passes[1].ResourceBindings[0].SourceFieldName, Is.EqualTo(TextureProducerPass.OutputFieldName));
                Assert.That(result.Passes[2].ResourceBindings, Has.Count.EqualTo(1));
                Assert.That(result.Passes[2].ResourceBindings[0].SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
                Assert.That(result.Passes[2].ResourceBindings[0].SourcePassIndex, Is.EqualTo(1));
                Assert.That(result.Passes[2].ResourceBindings[0].SourceFieldName, Is.EqualTo(TexturePassthroughPass.OutputFieldName));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_UsesExternalResourceBinding_WhenSubSystemConsumesOuterResource()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var rootTextureNode = new TextureResourceNodeData();
                var consumerNode = new TextureConsumerPassNode();
                graph.AddNode(rootTextureNode);
                RenderGraphTestUtility.AddTestNode(graph, consumerNode);

                var subgraphNode = CreateSubSystem(graph, out var subSystemGraph);
                var inputVariable = subSystemGraph.CreateVariable(
                    "InputTexture",
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Input);
                var outputVariable = subSystemGraph.CreateVariable(
                    "OutputTexture",
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Output);

                var inputVariableNode = subSystemGraph.AddVariableNode(inputVariable, new Vector2(50f, 50f));
                var outputVariableNode = subSystemGraph.AddVariableNode(outputVariable, new Vector2(350f, 50f));
                var innerPassNode = new TexturePassthroughPassNode();
                RenderGraphTestUtility.AddTestNode(subSystemGraph, innerPassNode);

                Assert.That(graph.Connect(
                    rootTextureNode.GetOutputPortByName(TextureResourceNodeData.OutputPortName),
                    GetRequiredInputPort(subgraphNode, inputVariable)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    GetRequiredOutputPort(inputVariableNode),
                    innerPassNode.GetInputPortByName(TexturePassthroughPass.InputFieldName)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    innerPassNode.GetOutputPortByName(TexturePassthroughPass.OutputFieldName),
                    GetRequiredInputPort(outputVariableNode)),
                    Is.True);
                Assert.That(graph.Connect(
                    GetRequiredOutputPort(subgraphNode, outputVariable),
                    consumerNode.GetInputPortByName(TextureConsumerPass.InputFieldName)),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.TextureDescriptors, Has.Count.EqualTo(1));
                Assert.That(result.Passes, Has.Count.EqualTo(2));
                Assert.That(result.Passes[0].ResourceBindings, Has.Count.EqualTo(1));
                Assert.That(result.Passes[0].ResourceBindings[0].SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.Resource));
                Assert.That(result.Passes[0].ResourceBindings[0].ResourceKind, Is.EqualTo(RenderGraphResourceKind.Texture));
                Assert.That(result.Passes[0].ResourceBindings[0].ResourceIndex, Is.EqualTo(0));
                Assert.That(result.Passes[1].ResourceBindings, Has.Count.EqualTo(1));
                Assert.That(result.Passes[1].ResourceBindings[0].SourceKind, Is.EqualTo(RenderGraphPassBindingSourceKind.PassField));
                Assert.That(result.Passes[1].ResourceBindings[0].SourcePassIndex, Is.EqualTo(0));
                Assert.That(result.Passes[1].ResourceBindings[0].SourceFieldName, Is.EqualTo(TexturePassthroughPass.OutputFieldName));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_IncludesPrivateSubSystemResources_InFlattenedDescriptors()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var subgraphNode = CreateSubSystem(graph, out var subSystemGraph);
                Assert.That(subgraphNode, Is.Not.Null);

                var textureNode = new TextureResourceNodeData();
                var bufferNode = new BufferResourceNodeData();
                var renderListNode = new RenderListResourceNodeData();
                var accelerationStructureNode = new AccelerationStructureResourceNodeData();
                var consumerNode = new PrivateResourcesConsumerPassNode();

                subSystemGraph.AddNode(textureNode);
                subSystemGraph.AddNode(bufferNode);
                subSystemGraph.AddNode(renderListNode);
                subSystemGraph.AddNode(accelerationStructureNode);
                RenderGraphTestUtility.AddTestNode(subSystemGraph, consumerNode);

                Assert.That(subSystemGraph.Connect(
                    textureNode.GetOutputPortByName(TextureResourceNodeData.OutputPortName),
                    consumerNode.GetInputPortByName(PrivateResourcesConsumerPass.TextureFieldName)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    bufferNode.GetOutputPortByName(BufferResourceNodeData.OutputPortName),
                    consumerNode.GetInputPortByName(PrivateResourcesConsumerPass.BufferFieldName)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    renderListNode.GetOutputPortByName(RenderListResourceNodeData.OutputPortName),
                    consumerNode.GetInputPortByName(PrivateResourcesConsumerPass.RenderListFieldName)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    accelerationStructureNode.GetOutputPortByName(AccelerationStructureResourceNodeData.OutputPortName),
                    consumerNode.GetInputPortByName(PrivateResourcesConsumerPass.AccelerationStructureFieldName)),
                    Is.True);
                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.Passes, Has.Count.EqualTo(1));
                Assert.That(result.TextureDescriptors, Has.Count.EqualTo(1));
                Assert.That(result.BufferDescriptors, Has.Count.EqualTo(1));
                Assert.That(result.RenderListDescriptors, Has.Count.EqualTo(1));
                Assert.That(result.AccelerationStructureDescriptors, Has.Count.EqualTo(1));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

    }

    public class RenderGraphSubSystemValidatorTests
    {
        [Test]
        public void Validate_LogsError_WhenSubSystemVariableUsesUnsupportedType()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var subgraphNode = CreateSubSystem(graph, out var subSystemGraph);
                Assert.That(subgraphNode, Is.Not.Null);

                subSystemGraph.CreateVariable("Label", typeof(string), string.Empty, VariableKind.Input);

                var sink = new TestErrorsAndWarnings();
                var logger = CreateLogger(sink);

                RenderGraphEditorValidator.Validate(subSystemGraph, logger);

                Assert.That(sink.Errors.Any(message => message.Contains("unsupported type")), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Validate_LogsError_WhenSubSystemContainsNestedSubSystem()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var subgraphNode = CreateSubSystem(graph, out var subSystemGraph);
                Assert.That(subgraphNode, Is.Not.Null);

                var nestedSubgraph = new NestedSubSystemStubNode();
                RenderGraphTestUtility.AddTestNode(subSystemGraph, nestedSubgraph);

                var sink = new TestErrorsAndWarnings();
                var logger = CreateLogger(sink);

                RenderGraphEditorValidator.Validate(subSystemGraph, logger);

                Assert.That(sink.Errors.Any(message => message.Contains("cannot contain nested")), Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }

    public class RenderGraphSubSystemImporterTests
    {
        [Test]
        public void Importer_FlattensSubSystemIntoExistingRuntimeAssetModel()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var producerNode = new TextureProducerPassNode();
                var consumerNode = new TextureConsumerPassNode();
                RenderGraphTestUtility.AddTestNode(graph, producerNode);
                RenderGraphTestUtility.AddTestNode(graph, consumerNode);

                var subgraphNode = CreateSubSystem(graph, out var subSystemGraph);
                var inputVariable = subSystemGraph.CreateVariable(
                    "InputTexture",
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Input);
                var outputVariable = subSystemGraph.CreateVariable(
                    "OutputTexture",
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Output);

                var inputVariableNode = subSystemGraph.AddVariableNode(inputVariable, new Vector2(50f, 50f));
                var outputVariableNode = subSystemGraph.AddVariableNode(outputVariable, new Vector2(350f, 50f));
                var innerPassNode = new TexturePassthroughPassNode();
                RenderGraphTestUtility.AddTestNode(subSystemGraph, innerPassNode);

                Assert.That(graph.Connect(
                    producerNode.GetOutputPortByName(TextureProducerPass.OutputFieldName),
                    GetRequiredInputPort(subgraphNode, inputVariable)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    GetRequiredOutputPort(inputVariableNode),
                    innerPassNode.GetInputPortByName(TexturePassthroughPass.InputFieldName)),
                    Is.True);
                Assert.That(subSystemGraph.Connect(
                    innerPassNode.GetOutputPortByName(TexturePassthroughPass.OutputFieldName),
                    GetRequiredInputPort(outputVariableNode)),
                    Is.True);
                Assert.That(graph.Connect(
                    GetRequiredOutputPort(subgraphNode, outputVariable),
                    consumerNode.GetInputPortByName(TextureConsumerPass.InputFieldName)),
                    Is.True);

                GraphDatabase.SaveGraph(graph);
                var assetPath = GraphDatabase.GetGraphAssetPath(graph);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var runtimeAsset = AssetDatabase.LoadAssetAtPath<RenderGraphData>(assetPath);

                Assert.That(runtimeAsset, Is.Not.Null);
                Assert.That(runtimeAsset.Passes, Has.Count.EqualTo(3));
                Assert.That(runtimeAsset.Passes.Select(pass => pass.PassType), Is.EqualTo(new[]
                {
                    GetPassTypeName<TextureProducerPass>(),
                    GetPassTypeName<TexturePassthroughPass>(),
                    GetPassTypeName<TextureConsumerPass>(),
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }
    }

    [Serializable]
    public sealed class TextureProducerPass : RasterPass
    {
        internal const string OutputFieldName = "m_OutputTexture";

        [RenderGraphResource(Name = "Output", Access = AccessFlags.Write)]
        private RenderGraphTexture m_OutputTexture = new RenderGraphTexture();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterPassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Serializable]
    public sealed class TexturePassthroughPass : RasterPass
    {
        internal const string InputFieldName = "m_InputTexture";
        internal const string OutputFieldName = "m_OutputTexture";

        [RenderGraphResource(Name = "Input", Access = AccessFlags.Read)]
        private RenderGraphTexture m_InputTexture = new RenderGraphTexture();

        [RenderGraphResource(Name = "Output", Access = AccessFlags.Write)]
        private RenderGraphTexture m_OutputTexture = new RenderGraphTexture();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterPassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Serializable]
    public sealed class TextureConsumerPass : RasterPass
    {
        internal const string InputFieldName = "m_InputTexture";

        [RenderGraphResource(Name = "Input", Access = AccessFlags.Read)]
        private RenderGraphTexture m_InputTexture = new RenderGraphTexture();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(RasterPassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Serializable]
    public sealed class PrivateResourcesConsumerPass : ComputePass
    {
        internal const string TextureFieldName = "m_Texture";
        internal const string BufferFieldName = "m_Buffer";
        internal const string RenderListFieldName = "m_RenderList";
        internal const string AccelerationStructureFieldName = "m_AccelerationStructure";

        [RenderGraphResource(Name = "Texture", Access = AccessFlags.Read)]
        private RenderGraphTexture m_Texture = new RenderGraphTexture();

        [RenderGraphResource(Name = "Buffer", Access = AccessFlags.Read)]
        private RenderGraphBuffer m_Buffer = new RenderGraphBuffer();

        [RenderGraphResource(Name = "RenderList", Access = AccessFlags.Read)]
        private RenderGraphRenderList m_RenderList = new RenderGraphRenderList();

        [RenderGraphResource(Name = "AccelerationStructure", Access = AccessFlags.Read)]
        private RenderGraphAccelerationStructure m_AccelerationStructure = new RenderGraphAccelerationStructure();

        public override void Create()
        {
        }

        public override void Prepare(ContextContainer frameData)
        {
        }

        public override void Record(ComputePassContext context)
        {
        }

        public override void Dispose()
        {
        }
    }

    [Serializable]
    internal sealed class TextureProducerPassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(TextureProducerPass);
    }

    [Serializable]
    internal sealed class TexturePassthroughPassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(TexturePassthroughPass);
    }

    [Serializable]
    internal sealed class TextureConsumerPassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(TextureConsumerPass);
    }

    [Serializable]
    internal sealed class PrivateResourcesConsumerPassNode : RenderPassNodeData
    {
        internal override Type GetRegisteredPassType() => typeof(PrivateResourcesConsumerPass);
    }

    [Serializable]
    internal sealed class NestedSubSystemStubNode : Node, ISubgraphNode
    {
        public Graph GetSubgraph()
        {
            return null;
        }
    }

    internal sealed class TestErrorsAndWarnings : IErrorsAndWarnings
    {
        internal readonly List<string> Errors = new List<string>();
        internal readonly List<string> Warnings = new List<string>();

        public void LogError(object message, object context)
        {
            Errors.Add(message?.ToString() ?? string.Empty);
        }

        public void LogWarning(object message, object context)
        {
            Warnings.Add(message?.ToString() ?? string.Empty);
        }

        public void Log(object message, object context)
        {
        }
    }

    internal static class RenderGraphSubSystemTestUtility
    {
        internal static ISubgraphNode CreateSubSystem(RenderGraphEditorGraph graph, out RenderGraphSubSystemGraph subSystemGraph)
        {
            var subgraphNode = graph.CreateLocalSubgraphNode<RenderGraphSubSystemGraph>("SubSystem", new Vector2(200f, 200f));
            subSystemGraph = subgraphNode?.GetSubgraph() as RenderGraphSubSystemGraph;
            return subgraphNode;
        }

        internal static IPort GetRequiredInputPort(ISubgraphNode subgraphNode, IVariable variable)
        {
            var success = RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(subgraphNode, variable, out var port);
            Assert.That(success, Is.True);
            Assert.That(port, Is.Not.Null);
            return port;
        }

        internal static IPort GetRequiredOutputPort(ISubgraphNode subgraphNode, IVariable variable)
        {
            var success = RenderGraphSubSystemReflectionUtility.TryGetOutputPortForVariable(subgraphNode, variable, out var port);
            Assert.That(success, Is.True);
            Assert.That(port, Is.Not.Null);
            return port;
        }

        internal static IPort GetRequiredInputPort(INode node)
        {
            Assert.That(node, Is.Not.Null);
            Assert.That(node.InputPortCount, Is.GreaterThan(0));
            return node.GetInputPort(0);
        }

        internal static IPort GetRequiredOutputPort(INode node)
        {
            Assert.That(node, Is.Not.Null);
            Assert.That(node.OutputPortCount, Is.GreaterThan(0));
            return node.GetOutputPort(0);
        }

        internal static string GetPassTypeName<T>()
        {
            var type = typeof(T);
            return $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        internal static GraphLogger CreateLogger(IErrorsAndWarnings sink)
        {
            var logger = new GraphLogger();
            var property = typeof(GraphLogger).GetProperty(
                "errorsAndWarnings",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property?.SetValue(logger, sink);
            return logger;
        }
    }
}
