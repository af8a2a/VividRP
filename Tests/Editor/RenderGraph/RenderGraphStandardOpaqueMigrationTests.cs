using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class RenderGraphStandardOpaqueMigrationTests
    {
        [Serializable]
        private sealed class PreDepthNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(PreDepthPass);
        }

        [Serializable]
        private sealed class GBufferNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(GBufferPass);
        }

        [Serializable]
        private sealed class MaterialDebugNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(MaterialDebugPass);
        }

        [Serializable]
        private sealed class ClassificationNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(MaterialClassificationPass);
        }

        [Serializable]
        private sealed class DeferredNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(DeferredLightingPass);
        }

        [Serializable]
        private sealed class HzbNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(HZBGeneratePass);
        }

        [Serializable]
        private sealed class VisibilityNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VisibilityBufferPass);
        }

        [Serializable]
        private sealed class ResolveNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VisibilityBufferGBufferResolvePass);
        }

        [Serializable]
        private sealed class LegacyResolveNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VisibilityBufferResolvePass);
        }

        [Test]
        public void Migrate_RewiresStandardTopology_AndDisconnectsLegacyProducerForPersistedCleanup()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var topology = CreateLegacyStandardTopology(graph);

                Assert.That(
                    RenderGraphStandardOpaqueMigration.Migrate(graph, "Assets/Test.vrdg"),
                    Is.True);

                var passNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
                var visibilityNode = FindPass(passNodes, typeof(VisibilityBufferPass));
                var resolveNode = FindPass(passNodes, typeof(VisibilityBufferGBufferResolvePass));

                Assert.That(visibilityNode, Is.Not.Null);
                Assert.That(resolveNode, Is.Not.Null);
                var disconnectedGBuffer = FindPass(passNodes, typeof(GBufferPass));
                Assert.That(disconnectedGBuffer, Is.Not.Null);
                Assert.That(((INode)disconnectedGBuffer).IsConnected, Is.False);
                Assert.That(FindPass(passNodes, typeof(VisibilityBufferResolvePass)), Is.Null);
                AssertConnected(
                    topology.PreDepth.GetOutputPortByName("m_DepthAttachment_Out"),
                    visibilityNode.GetInputPortByName("m_Depth_In"));
                AssertConnected(
                    visibilityNode.GetOutputPortByName("m_Depth_Out"),
                    topology.Hzb.GetInputPortByName("m_DepthTexture"));
                AssertConnected(
                    visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                    resolveNode.GetInputPortByName("m_VisibilityBuffer"));
                AssertConnected(
                    visibilityNode.GetOutputPortByName("m_Attributes0_Out"),
                    resolveNode.GetInputPortByName("m_Attributes0"));
                AssertConnected(
                    visibilityNode.GetOutputPortByName("m_Attributes1_Out"),
                    resolveNode.GetInputPortByName("m_Attributes1"));
                AssertConnected(
                    visibilityNode.GetOutputPortByName("m_Barycentrics_Out"),
                    resolveNode.GetInputPortByName("m_Barycentrics"));

                Assert.That(topology.DiffuseIrradianceVariable.Name, Is.EqualTo("DiffuseIrradiance (R)"));
                Assert.That(
                    RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(
                        topology.SubSystemNode,
                        topology.DiffuseIrradianceVariable,
                        out var diffuseIrradianceInterfacePort),
                    Is.True);
                AssertConnected(
                    resolveNode.GetOutputPortByName("m_GBuffer4_Out"),
                    diffuseIrradianceInterfacePort);
                AssertConnected(
                    GetVariableOutput(topology.DiffuseIrradianceVariable),
                    topology.Deferred.GetInputPortByName("m_GBuffer4"));
                AssertConnected(
                    GetVariableOutput(topology.GBuffer1Variable),
                    topology.Classification.GetInputPortByName("m_GBuffer1"));
                AssertConnected(
                    resolveNode.GetOutputPortByName("m_GBuffer4_Out"),
                    topology.MaterialDebug.GetInputPortByName("m_GBuffer4"));
                AssertConnected(
                    visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                    topology.MaterialDebug.GetInputPortByName("m_VisibilityBuffer"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_ReusesExistingVisibilityAndResolveNodes_InHybridTopology()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                CreateLegacyStandardTopology(graph);
                var visibilityNode = new VisibilityNode();
                var resolveNode = new ResolveNode();
                RenderGraphTestUtility.AddTestNode(graph, visibilityNode);
                RenderGraphTestUtility.AddTestNode(graph, resolveNode);

                Assert.That(
                    RenderGraphStandardOpaqueMigration.Migrate(graph, "Assets/Test.vrdg"),
                    Is.True);

                var passNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
                Assert.That(
                    passNodes.Count(node => node.GetPassType() == typeof(VisibilityBufferPass)),
                    Is.EqualTo(1));
                Assert.That(
                    passNodes.Count(node => node.GetPassType() == typeof(VisibilityBufferGBufferResolvePass)),
                    Is.EqualTo(1));
                Assert.That(FindPass(passNodes, typeof(VisibilityBufferPass)), Is.SameAs(visibilityNode));
                Assert.That(
                    FindPass(passNodes, typeof(VisibilityBufferGBufferResolvePass)),
                    Is.SameAs(resolveNode));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_ReentersSchema2CurrentTopology_AndConnectsAbiMarkerInput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var preDepthNode = new PreDepthNode();
                var materialDebugNode = new MaterialDebugNode();
                var visibilityNode = new VisibilityNode();
                var resolveNode = new ResolveNode();
                RenderGraphTestUtility.AddTestNode(graph, preDepthNode);
                RenderGraphTestUtility.AddTestNode(graph, materialDebugNode);
                RenderGraphTestUtility.AddTestNode(graph, visibilityNode);
                RenderGraphTestUtility.AddTestNode(graph, resolveNode);

                var subSystemNode = RenderGraphSubSystemTestUtility.CreateSubSystem(
                    graph,
                    out var subSystem);
                var classificationNode = new ClassificationNode();
                var deferredNode = new DeferredNode();
                RenderGraphTestUtility.AddTestNode(subSystem, classificationNode);
                RenderGraphTestUtility.AddTestNode(subSystem, deferredNode);

                var variables = new IVariable[5];
                for (var index = 0; index < variables.Length; index++)
                {
                    var name = index == variables.Length - 1
                        ? "DiffuseIrradiance (R)"
                        : $"GBuffer{index} (R)";
                    variables[index] = subSystem.CreateVariable(
                        name,
                        typeof(RenderGraphTexture),
                        new RenderGraphTexture(),
                        VariableKind.Input);
                    subSystem.AddVariableNode(variables[index], default);
                    Assert.That(
                        RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(
                            subSystemNode,
                            variables[index],
                            out var interfacePort),
                        Is.True);
                    Assert.That(graph.Connect(
                        resolveNode.GetOutputPortByName($"m_GBuffer{index}_Out"),
                        interfacePort), Is.True);
                }

                Assert.That(graph.Connect(
                    preDepthNode.GetOutputPortByName("m_DepthAttachment_Out"),
                    visibilityNode.GetInputPortByName("m_Depth_In")), Is.True);
                Assert.That(graph.Connect(
                    visibilityNode.GetOutputPortByName("m_VisibilityBuffer_Out"),
                    resolveNode.GetInputPortByName("m_VisibilityBuffer")), Is.True);
                Assert.That(graph.Connect(
                    visibilityNode.GetOutputPortByName("m_Attributes0_Out"),
                    resolveNode.GetInputPortByName("m_Attributes0")), Is.True);
                Assert.That(graph.Connect(
                    visibilityNode.GetOutputPortByName("m_Attributes1_Out"),
                    resolveNode.GetInputPortByName("m_Attributes1")), Is.True);
                Assert.That(graph.Connect(
                    visibilityNode.GetOutputPortByName("m_Barycentrics_Out"),
                    resolveNode.GetInputPortByName("m_Barycentrics")), Is.True);
                Assert.That(subSystem.Connect(
                    GetVariableOutput(variables[0]),
                    classificationNode.GetInputPortByName("m_GBuffer0")), Is.True);
                Assert.That(
                    classificationNode.GetInputPortByName("m_GBuffer1").IsConnected,
                    Is.False);

                graph.SchemaVersion = 2;
                Assert.That(
                    RenderGraphDrawObjectPassMigration.Migrate(
                        graph,
                        "Assets/Schema2.vrdg"),
                    Is.True);
                Assert.That(
                    graph.SchemaVersion,
                    Is.EqualTo(RenderGraphEditorGraph.CurrentSchemaVersion));
                AssertConnected(
                    GetVariableOutput(variables[1]),
                    classificationNode.GetInputPortByName("m_GBuffer1"));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_DoesNotChangeIsolatedCustomGBufferGraph()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var gBufferNode = new GBufferNode();
                RenderGraphTestUtility.AddTestNode(graph, gBufferNode);

                Assert.That(
                    RenderGraphStandardOpaqueMigration.Migrate(graph, "Assets/Test.vrdg"),
                    Is.False);
                Assert.That(graph.GetNodes().Contains(gBufferNode), Is.True);
                Assert.That(
                    graph.GetNodes().OfType<RenderPassNodeData>()
                        .Any(node => node.GetPassType() == typeof(VisibilityBufferPass)),
                    Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_DoesNotChangeCustomGraphWithStandardNodeSetButDifferentWiring()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var topology = CreateLegacyStandardTopology(graph);
                var depthOutput = topology.PreDepth.GetOutputPortByName("m_DepthAttachment_Out");
                var depthInput = topology.GBuffer.GetInputPortByName("m_GBufferDepth_In");
                Assert.That(graph.Disconnect(depthOutput, depthInput), Is.True);

                Assert.That(
                    RenderGraphStandardOpaqueMigration.Migrate(graph, "Assets/Custom.vrdg"),
                    Is.False);
                Assert.That(graph.GetNodes().Contains(topology.GBuffer), Is.True);
                Assert.That(
                    graph.GetNodes().OfType<RenderPassNodeData>()
                        .Any(node => node.GetPassType() == typeof(VisibilityBufferPass)),
                    Is.False);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_PreservesCustomInputsOutsideLegacyProducer()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var topology = CreateLegacyStandardTopology(graph);
                var legacyDepthOutput = topology.GBuffer.GetOutputPortByName("m_GBufferDepth_Out");
                var customDepthOutput = topology.PreDepth.GetOutputPortByName("m_DepthAttachment_Out");
                var hzbDepthInput = topology.Hzb.GetInputPortByName("m_DepthTexture");
                Assert.That(graph.Disconnect(legacyDepthOutput, hzbDepthInput), Is.True);
                Assert.That(graph.Connect(customDepthOutput, hzbDepthInput), Is.True);

                var hzbOutput = topology.Hzb.GetOutputPortByName("m_HzbTexture");
                var debugGBuffer1Input = topology.MaterialDebug.GetInputPortByName("m_GBuffer1");
                var debugVisibilityInput = topology.MaterialDebug.GetInputPortByName("m_VisibilityBuffer");
                Assert.That(graph.Connect(hzbOutput, debugGBuffer1Input), Is.True);
                Assert.That(graph.Connect(hzbOutput, debugVisibilityInput), Is.True);

                var subSystem = (RenderGraphSubSystemGraph)topology.SubSystemNode.GetSubgraph();
                var classificationGBuffer1Input = topology.Classification.GetInputPortByName("m_GBuffer1");
                var deferredDiffuseIrradianceInput = topology.Deferred.GetInputPortByName("m_GBuffer4");
                var diffuseIrradianceOutput = GetVariableOutput(topology.DiffuseIrradianceVariable);
                var gBuffer1Output = GetVariableOutput(topology.GBuffer1Variable);
                Assert.That(subSystem.Connect(diffuseIrradianceOutput, classificationGBuffer1Input), Is.True);
                Assert.That(
                    subSystem.Disconnect(diffuseIrradianceOutput, deferredDiffuseIrradianceInput),
                    Is.True);
                Assert.That(subSystem.Connect(gBuffer1Output, deferredDiffuseIrradianceInput), Is.True);

                Assert.That(
                    RenderGraphStandardOpaqueMigration.Migrate(graph, "Assets/Custom.vrdg"),
                    Is.True);

                AssertConnected(customDepthOutput, hzbDepthInput);
                AssertConnected(hzbOutput, debugGBuffer1Input);
                AssertConnected(hzbOutput, debugVisibilityInput);
                AssertConnected(diffuseIrradianceOutput, classificationGBuffer1Input);
                AssertConnected(gBuffer1Output, deferredDiffuseIrradianceInput);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Migrate_DoesNotRemoveConnectedLegacyDebugResolve()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var topology = CreateLegacyStandardTopology(graph);
                Assert.That(graph.Connect(
                    topology.GBuffer.GetOutputPortByName("m_GBuffer0"),
                    topology.LegacyResolve.GetInputPortByName("m_VisibilityBuffer")), Is.True);

                Assert.That(
                    RenderGraphStandardOpaqueMigration.Migrate(graph, "Assets/Custom.vrdg"),
                    Is.False);
                Assert.That(graph.GetNodes().Contains(topology.LegacyResolve), Is.True);
                Assert.That(topology.LegacyResolve.GetInputPortByName("m_VisibilityBuffer").IsConnected, Is.True);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static LegacyStandardTopology CreateLegacyStandardTopology(
            RenderGraphEditorGraph graph)
        {
            var preDepthNode = new PreDepthNode();
            var gBufferNode = new GBufferNode();
            var materialDebugNode = new MaterialDebugNode();
            var hzbNode = new HzbNode();
            var legacyResolveNode = new LegacyResolveNode();
            RenderGraphTestUtility.AddTestNode(graph, preDepthNode);
            RenderGraphTestUtility.AddTestNode(graph, gBufferNode);
            RenderGraphTestUtility.AddTestNode(graph, materialDebugNode);
            RenderGraphTestUtility.AddTestNode(graph, hzbNode);
            RenderGraphTestUtility.AddTestNode(graph, legacyResolveNode);

            var subSystemNode = RenderGraphSubSystemTestUtility.CreateSubSystem(
                graph,
                out var subSystem);
            var classificationNode = new ClassificationNode();
            var deferredNode = new DeferredNode();
            RenderGraphTestUtility.AddTestNode(subSystem, classificationNode);
            RenderGraphTestUtility.AddTestNode(subSystem, deferredNode);

            var variables = new IVariable[5];
            for (var index = 0; index < variables.Length; index++)
            {
                var name = index == variables.Length - 1
                    ? "GBuffer4 (R)"
                    : $"GBuffer{index} (R)";
                variables[index] = subSystem.CreateVariable(
                    name,
                    typeof(RenderGraphTexture),
                    new RenderGraphTexture(),
                    VariableKind.Input);
                subSystem.AddVariableNode(variables[index], default);
            }

            Assert.That(graph.Connect(
                preDepthNode.GetOutputPortByName("m_DepthAttachment_Out"),
                gBufferNode.GetInputPortByName("m_GBufferDepth_In")), Is.True);
            Assert.That(graph.Connect(
                gBufferNode.GetOutputPortByName("m_GBufferDepth_Out"),
                hzbNode.GetInputPortByName("m_DepthTexture")), Is.True);

            for (var index = 0; index < variables.Length; index++)
            {
                Assert.That(
                    RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(
                        subSystemNode,
                        variables[index],
                        out var interfacePort),
                    Is.True);
                Assert.That(graph.Connect(
                    gBufferNode.GetOutputPortByName($"m_GBuffer{index}"),
                    interfacePort), Is.True);
            }

            Assert.That(subSystem.Connect(
                GetVariableOutput(variables[0]),
                classificationNode.GetInputPortByName("m_GBuffer0")), Is.True);
            Assert.That(subSystem.Connect(
                GetVariableOutput(variables[0]),
                deferredNode.GetInputPortByName("m_GBuffer0")), Is.True);
            Assert.That(subSystem.Connect(
                GetVariableOutput(variables[1]),
                deferredNode.GetInputPortByName("m_GBuffer1")), Is.True);
            Assert.That(subSystem.Connect(
                GetVariableOutput(variables[2]),
                deferredNode.GetInputPortByName("m_GBuffer2")), Is.True);
            Assert.That(subSystem.Connect(
                GetVariableOutput(variables[3]),
                deferredNode.GetInputPortByName("m_GBuffer3")), Is.True);
            Assert.That(subSystem.Connect(
                GetVariableOutput(variables[4]),
                deferredNode.GetInputPortByName("m_GBuffer4")), Is.True);

            Assert.That(graph.Connect(
                gBufferNode.GetOutputPortByName("m_GBuffer0"),
                materialDebugNode.GetInputPortByName("m_GBuffer0")), Is.True);
            Assert.That(graph.Connect(
                gBufferNode.GetOutputPortByName("m_GBuffer4"),
                materialDebugNode.GetInputPortByName("m_GBuffer4")), Is.True);

            return new LegacyStandardTopology(
                preDepthNode,
                gBufferNode,
                materialDebugNode,
                hzbNode,
                legacyResolveNode,
                subSystemNode,
                classificationNode,
                deferredNode,
                variables[1],
                variables[4]);
        }

        private static RenderPassNodeData FindPass(
            IEnumerable<RenderPassNodeData> nodes,
            Type passType)
        {
            return nodes.FirstOrDefault(node => node.GetPassType() == passType);
        }

        private static IPort GetVariableOutput(IVariable variable)
        {
            var variableNodes = new List<IVariableNode>();
            variable.GetNodes(variableNodes);
            Assert.That(variableNodes, Has.Count.EqualTo(1));
            return variableNodes[0].GetOutputPort(0);
        }

        private static void AssertConnected(IPort expectedOutput, IPort input)
        {
            Assert.That(expectedOutput, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(input.FirstConnectedPort, Is.SameAs(expectedOutput));
        }

        private sealed class LegacyStandardTopology
        {
            internal LegacyStandardTopology(
                RenderPassNodeData preDepth,
                RenderPassNodeData gBuffer,
                RenderPassNodeData materialDebug,
                RenderPassNodeData hzb,
                RenderPassNodeData legacyResolve,
                ISubgraphNode subSystemNode,
                RenderPassNodeData classification,
                RenderPassNodeData deferred,
                IVariable gBuffer1Variable,
                IVariable diffuseIrradianceVariable)
            {
                PreDepth = preDepth;
                GBuffer = gBuffer;
                MaterialDebug = materialDebug;
                Hzb = hzb;
                LegacyResolve = legacyResolve;
                SubSystemNode = subSystemNode;
                Classification = classification;
                Deferred = deferred;
                GBuffer1Variable = gBuffer1Variable;
                DiffuseIrradianceVariable = diffuseIrradianceVariable;
            }

            internal RenderPassNodeData PreDepth { get; }
            internal RenderPassNodeData GBuffer { get; }
            internal RenderPassNodeData MaterialDebug { get; }
            internal RenderPassNodeData Hzb { get; }
            internal RenderPassNodeData LegacyResolve { get; }
            internal ISubgraphNode SubSystemNode { get; }
            internal RenderPassNodeData Classification { get; }
            internal RenderPassNodeData Deferred { get; }
            internal IVariable GBuffer1Variable { get; }
            internal IVariable DiffuseIrradianceVariable { get; }
        }
    }
}
