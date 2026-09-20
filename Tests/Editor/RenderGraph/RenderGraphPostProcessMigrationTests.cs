using System;
using System.Linq;
using NUnit.Framework;
using Unity.GraphToolkit.Editor;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderGraphPostProcessMigrationTests
    {
        [Test]
        public void Migrate_PreservesEffectsAndGuidesInSeparatePasses_AndIsIdempotent()
        {
            var graph = RenderGraphTestUtility.CreateGraph();
            try
            {
                graph.SchemaVersion = 4;
                var aa = Add<AntialiasingPass>(graph);
                var bloom = Add<BloomPass>(graph);
                var grading = Add<ColorGradingPass>(graph);
                var final = Add<FinalBlitPass>(graph);
                var color = new TextureResourceNodeData();
                var depth = new TextureResourceNodeData();
                var motion = new TextureResourceNodeData();
                graph.AddNode(color);
                graph.AddNode(depth);
                graph.AddNode(motion);
                var colorOutput = color.GetOutputPortByName(TextureResourceNodeData.OutputPortName);
                var depthOutput = depth.GetOutputPortByName(TextureResourceNodeData.OutputPortName);
                var motionOutput = motion.GetOutputPortByName(TextureResourceNodeData.OutputPortName);
                graph.Connect(colorOutput, aa.GetInputPortByName("Color"));
                graph.Connect(depthOutput, aa.GetInputPortByName("CameraDepth"));
                graph.Connect(motionOutput, aa.GetInputPortByName("MotionVectors"));
                graph.Connect(aa.GetOutputPortByName("AntialiasingOutput"), final.GetInputPortByName("source"));
                graph.Connect(bloom.GetOutputPortByName("bloomTexture"), final.GetInputPortByName("bloomTexture"));
                var lut = grading.GetOutputPorts().Single();
                graph.Connect(lut, final.GetInputPortByName("colorGradingLut"));

                Assert.That(RenderGraphPostProcessMigration.Migrate(graph), Is.True);
                var uber = Find<UberPostPass>(graph);
                var nr = Find<DLSSNeuralRenderingPass>(graph);
                Assert.That(uber.GetInputPortByName("source").FirstConnectedPort.GetNode(), Is.SameAs(aa));
                Assert.That(uber.GetInputPortByName("colorGradingLut").FirstConnectedPort, Is.EqualTo(lut));
                Assert.That(uber.GetInputPortByName("bloomTexture").FirstConnectedPort.GetNode(), Is.SameAs(bloom));
                Assert.That(nr.GetInputPortByName("m_Source").FirstConnectedPort.GetNode(), Is.SameAs(uber));
                Assert.That(nr.GetInputPortByName("m_Depth").FirstConnectedPort, Is.EqualTo(depthOutput));
                Assert.That(nr.GetInputPortByName("m_MotionVectors").FirstConnectedPort, Is.EqualTo(motionOutput));
                Assert.That(final.GetInputPortByName("source").FirstConnectedPort.GetNode(), Is.SameAs(nr));
                Assert.That(RenderGraphPostProcessMigration.Migrate(graph), Is.False);

                graph.SchemaVersion = RenderGraphEditorGraph.CurrentSchemaVersion;
                final.DefineNode();
                Assert.That(final.GetInputPortByName("colorGradingLut"), Is.Null);
                var compiled = RenderGraphCompiler.Compile(graph);
                var names = compiled.Passes.Select(pass => pass.PassType).ToArray();
                var uberIndex = Array.FindIndex(names, name => name.StartsWith(typeof(UberPostPass).FullName + ","));
                var nrIndex = Array.FindIndex(names, name => name.StartsWith(typeof(DLSSNeuralRenderingPass).FullName + ","));
                var finalIndex = Array.FindIndex(names, name => name.StartsWith(typeof(FinalBlitPass).FullName + ","));
                Assert.That(uberIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(nrIndex, Is.GreaterThan(uberIndex));
                Assert.That(finalIndex, Is.GreaterThan(nrIndex));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        private static RenderPassNodeData Add<T>(RenderGraphEditorGraph graph)
        {
            var node = (RenderPassNodeData)Activator.CreateInstance(RenderPassNodeRegistry.GetNodeType(typeof(T)));
            graph.AddNode(node);
            return node;
        }

        private static RenderPassNodeData Find<T>(RenderGraphEditorGraph graph)
        {
            return graph.GetNodes().OfType<RenderPassNodeData>().Single(node => node.GetPassType() == typeof(T));
        }
    }
}
