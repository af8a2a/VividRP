using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEngine;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphPostProcessMigration
    {
        internal const int SchemaVersion = 5;

        internal static bool Migrate(RenderGraphEditorGraph graph)
        {
            if (graph.SchemaVersion >= SchemaVersion)
                return false;

            var passes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
            var changed = false;
            foreach (var finalBlit in passes.Where(node => node.GetPassType() == typeof(FinalBlitPass)))
            {
                var source = finalBlit.GetInputPortByName("source")?.FirstConnectedPort;
                if (source == null)
                    continue;

                var sourcePassType = (source.GetNode() as RenderPassNodeData)?.GetPassType();
                if (sourcePassType == typeof(UberPostPass) || sourcePassType == typeof(DLSSNeuralRenderingPass))
                    continue;

                var uber = CreatePass(graph, typeof(UberPostPass), finalBlit.Position + new Vector2(-640f, 0f));
                var nr = CreatePass(graph, typeof(DLSSNeuralRenderingPass), finalBlit.Position + new Vector2(-320f, 0f));
                MoveInput(graph, finalBlit, uber, "source");
                MoveInput(graph, finalBlit, uber, "colorGradingLut");
                MoveInput(graph, finalBlit, uber, "bloomTexture");

                // The old FinalBlit received these guides through AntialiasingData.
                // Preserve the actual graph connections as explicit NR inputs.
                var aa = FindUpstreamAntialiasingPass(source);
                if (aa != null)
                {
                    ConnectGuide(graph, aa, "CameraDepth", nr, "m_Depth");
                    ConnectGuide(graph, aa, "MotionVectors", nr, "m_MotionVectors");
                }

                graph.Connect(uber.GetOutputPortByName("m_OutputTexture"), nr.GetInputPortByName("m_Source"));
                graph.Connect(nr.GetOutputPortByName("m_Output"), finalBlit.GetInputPortByName("source"));
                changed = true;
            }

            return changed;
        }

        private static RenderPassNodeData CreatePass(RenderGraphEditorGraph graph, Type passType, Vector2 position)
        {
            var node = (RenderPassNodeData)Activator.CreateInstance(RenderPassNodeRegistry.GetNodeType(passType));
            node.Position = position;
            graph.AddNode(node);
            return node;
        }

        private static void MoveInput(RenderGraphEditorGraph graph, RenderPassNodeData from, RenderPassNodeData to, string name)
        {
            var input = from.GetInputPortByName(name);
            var output = input?.FirstConnectedPort;
            if (output == null)
                return;

            graph.Disconnect(output, input);
            graph.Connect(output, to.GetInputPortByName(name));
        }

        private static RenderPassNodeData FindUpstreamAntialiasingPass(IPort output)
        {
            var visited = new System.Collections.Generic.HashSet<INode>();
            while (output?.GetNode() is RenderPassNodeData node && visited.Add(node))
            {
                if (node.GetPassType() == typeof(AntialiasingPass))
                    return node;

                output = (node.GetInputPortByName("source")
                    ?? node.GetInputPortByName("m_Source")
                    ?? node.GetInputPortByName("m_SourceTexture")
                    ?? node.GetInputPortByName("source_In")
                    ?? node.GetInputPortByName("Color"))?.FirstConnectedPort;
            }

            return null;
        }

        private static void ConnectGuide(RenderGraphEditorGraph graph, RenderPassNodeData aa, string name, RenderPassNodeData nr, string target)
        {
            var output = aa.GetInputPortByName(name)?.FirstConnectedPort;
            if (output != null)
                graph.Connect(output, nr.GetInputPortByName(target));
        }
    }
}
