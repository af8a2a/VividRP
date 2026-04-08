using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public sealed class DDGIPassNodeTests
    {
        [Serializable]
        private sealed class AutoRegisteredDDGIRTASBuildPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DDGIRTASBuildPass).AssemblyQualifiedName;
        }

        [Serializable]
        private sealed class AutoRegisteredDDGIProbeTracePassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DDGIProbeTracePass).AssemblyQualifiedName;
        }

        [Serializable]
        private sealed class AutoRegisteredDDGIProbeBlendPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DDGIProbeBlendPass).AssemblyQualifiedName;
        }

        [Serializable]
        private sealed class AutoRegisteredDeferredLightingPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DeferredLightingPass).AssemblyQualifiedName;
        }

        [Test]
        public void DDGIRTASBuildPassNode_DefinesAccelerationStructureOutput()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDDGIRTASBuildPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetOutputPortByName("m_DDGIAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DDGIAccelerationStructure_In"), Is.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DDGIProbeTracePassNode_DefinesRTASInputAndProbeOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDDGIProbeTracePassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_DDGIAccelerationStructure"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeRayData"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeIrradiance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeData"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DDGIProbeBlendPassNode_DefinesProbeInputsAndAtlasOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDDGIProbeBlendPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_ProbeRayData"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_ProbeData"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeIrradiance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeDistance"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_ProbeVariability"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void DeferredLightingPassNode_DefinesDDGIOrderingInputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredDeferredLightingPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_DDGIProbeIrradiance"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DDGIProbeDistance"), Is.Not.Null);
                Assert.That(node.GetInputPortByName("m_DDGIProbeData"), Is.Not.Null);
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void Compile_OrdersDDGIPassesBeforeDeferredLighting_WhenDeferredConsumesDDGIOutputs()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var buildNode = new AutoRegisteredDDGIRTASBuildPassNode();
                var traceNode = new AutoRegisteredDDGIProbeTracePassNode();
                var blendNode = new AutoRegisteredDDGIProbeBlendPassNode();
                var deferredNode = new AutoRegisteredDeferredLightingPassNode();

                RenderGraphTestUtility.AddTestNode(graph, buildNode);
                RenderGraphTestUtility.AddTestNode(graph, traceNode);
                RenderGraphTestUtility.AddTestNode(graph, blendNode);
                RenderGraphTestUtility.AddTestNode(graph, deferredNode);

                Assert.That(graph.Connect(
                    buildNode.GetOutputPortByName("m_DDGIAccelerationStructure"),
                    traceNode.GetInputPortByName("m_DDGIAccelerationStructure")),
                    Is.True);
                Assert.That(graph.Connect(
                    traceNode.GetOutputPortByName("m_ProbeRayData"),
                    blendNode.GetInputPortByName("m_ProbeRayData")),
                    Is.True);
                Assert.That(graph.Connect(
                    traceNode.GetOutputPortByName("m_ProbeData"),
                    blendNode.GetInputPortByName("m_ProbeData")),
                    Is.True);
                Assert.That(graph.Connect(
                    blendNode.GetOutputPortByName("m_ProbeIrradiance"),
                    deferredNode.GetInputPortByName("m_DDGIProbeIrradiance")),
                    Is.True);
                Assert.That(graph.Connect(
                    blendNode.GetOutputPortByName("m_ProbeDistance"),
                    deferredNode.GetInputPortByName("m_DDGIProbeDistance")),
                    Is.True);
                Assert.That(graph.Connect(
                    traceNode.GetOutputPortByName("m_ProbeData"),
                    deferredNode.GetInputPortByName("m_DDGIProbeData")),
                    Is.True);

                var result = RenderGraphCompiler.Compile(graph);

                Assert.That(result.ExecutionOrder.Select(pass => pass.PassTypeName), Is.EqualTo(new[]
                {
                    nameof(DDGIRTASBuildPass),
                    nameof(DDGIProbeTracePass),
                    nameof(DDGIProbeBlendPass),
                    nameof(DeferredLightingPass),
                }));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void GeneratedNodeRegistry_ContainsDDGIPassNodes()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "RenderGraph", "GeneratedRenderPassNodes.g.cs"));

            Assert.That(source, Does.Contain("internal sealed class DDGIRTASBuildPass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.DDGIRTASBuildPass, VividRP.Runtime"));
            Assert.That(source, Does.Contain("internal sealed class DDGIProbeTracePass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.DDGIProbeTracePass, VividRP.Runtime"));
            Assert.That(source, Does.Contain("internal sealed class DDGIProbeBlendPass : RenderPassNodeData"));
            Assert.That(source, Does.Contain("VividRP.Runtime.RenderPass.Core.DDGIProbeBlendPass, VividRP.Runtime"));
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] packageRoots =
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (string packageRoot in packageRoots)
            {
                string fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
