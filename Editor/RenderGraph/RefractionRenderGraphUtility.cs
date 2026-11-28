using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.RenderGraph
{
    /// <summary>
    /// GraphToolkit utility to splice the 3 standard Refraction passes
    /// (OpaqueColorPyramidPass + TransparentBackfacePass -> ForwardRefractionPass)
    /// directly after SubsurfaceScatteringPass (or DeferredLightingPass) in any VividRP .vrdg graph.
    /// </summary>
    public static class RefractionRenderGraphUtility
    {
        public static bool AddRefractionPasses(string graphPath)
        {
            var graph = GraphDatabase.LoadGraph<RenderGraphEditorGraph>(graphPath)
                ?? throw new ArgumentException($"Expected a VividRP Render Graph asset at '{graphPath}'.", nameof(graphPath));

            var scopes = CollectOwnedScopes(graph, graphPath);
            var lightingScope = scopes.FirstOrDefault(s =>
                s.GetNodes().OfType<RenderPassNodeData>().Any(n => n.GetPassType() == typeof(DeferredLightingPass)));
            if (lightingScope == null)
                throw new InvalidOperationException($"Could not find a scope containing {nameof(DeferredLightingPass)} in '{graphPath}'.");

            var lightingNodes = lightingScope.GetNodes().OfType<RenderPassNodeData>().ToArray();
            var deferred = lightingNodes.Single(n => n.GetPassType() == typeof(DeferredLightingPass));
            var sssNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(SubsurfaceScatteringPass));
            var lightGrid = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(LightGridPass));
            var colorProducer = sssNode ?? deferred;

            IPort colorProducerOut = colorProducer.GetOutputPortByName("m_ColorTexture_Out")
                ?? colorProducer.GetOutputPortByName("m_ColorTexture");
            IPort depthSource = deferred.GetInputPortByName("m_DepthTexture")?.FirstConnectedPort;
            IPort shadowSource = deferred.GetInputPortByName("m_DirectionalShadowTexture")?.FirstConnectedPort;

            if (colorProducerOut == null || depthSource == null)
                throw new InvalidOperationException($"Failed to resolve SceneColor or Depth source in '{graphPath}'.");

            bool changed = false;

            // 1. OpaqueColorPyramidPass
            var pyramidNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(OpaqueColorPyramidPass));
            if (pyramidNode == null)
            {
                pyramidNode = CreatePassNode(lightingScope, typeof(OpaqueColorPyramidPass), colorProducer.Position + new Vector2(280f, 260f));
                changed = true;
            }
            pyramidNode.DefineNode();
            changed |= ConnectIfMissing(lightingScope, colorProducerOut, pyramidNode.GetInputPortByName("m_ColorTexture"));
            changed |= ConnectIfMissing(lightingScope, depthSource, pyramidNode.GetInputPortByName("m_DepthTexture"));

            // 2. TransparentBackfacePass
            var backfaceNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(TransparentBackfacePass));
            if (backfaceNode == null)
            {
                backfaceNode = CreatePassNode(lightingScope, typeof(TransparentBackfacePass), colorProducer.Position + new Vector2(280f, 480f));
                changed = true;
            }
            backfaceNode.DefineNode();

            // 3. ForwardRefractionPass (spliced into SceneColor chain after SubsurfaceScatteringPass / DeferredLightingPass)
            var refractNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(ForwardRefractionPass));
            if (refractNode == null)
            {
                refractNode = CreatePassNode(lightingScope, typeof(ForwardRefractionPass), colorProducer.Position + new Vector2(620f, 120f));
                changed = true;
            }
            refractNode.DefineNode();

            IPort refractColorIn = refractNode.GetInputPortByName("m_ColorTexture_In")
                ?? refractNode.GetInputPortByName("m_ColorTexture");
            IPort refractColorOut = refractNode.GetOutputPortByName("m_ColorTexture_Out")
                ?? refractNode.GetOutputPortByName("m_ColorTexture");
            IPort refractDepthIn = refractNode.GetInputPortByName("m_DepthTexture_In")
                ?? refractNode.GetInputPortByName("m_DepthTexture");

            if (refractColorIn == null || refractColorOut == null || refractDepthIn == null)
                throw new InvalidOperationException($"Failed to resolve ForwardRefractionPass color/depth ports in '{graphPath}'.");

            // Splice ForwardRefractionPass into downstream SceneColor consumers (preserving OpaqueColorPyramidPass as sibling reader!)
            var downstreamConsumers = new List<IPort>();
            colorProducerOut.GetConnectedPorts(downstreamConsumers);
            bool alreadySpliced = downstreamConsumers.Any(p => ReferenceEquals(p.GetNode(), refractNode));
            if (!alreadySpliced)
            {
                foreach (var consumer in downstreamConsumers)
                {
                    if (ReferenceEquals(consumer.GetNode(), refractNode)
                        || ReferenceEquals(consumer.GetNode(), pyramidNode))
                    {
                        continue;
                    }

                    lightingScope.Disconnect(colorProducerOut, consumer);
                    if (!lightingScope.Connect(refractColorOut, consumer))
                        throw new InvalidOperationException($"Failed to rewire SceneColor consumer '{consumer.GetNode()}.{consumer.Name}' to ForwardRefractionPass.");
                }

                if (!lightingScope.Connect(colorProducerOut, refractColorIn))
                    throw new InvalidOperationException("Failed to connect SceneColor -> ForwardRefractionPass.");
                changed = true;
            }

            changed |= ConnectIfMissing(lightingScope, depthSource, refractDepthIn);

            IPort pyramidOut = pyramidNode.GetOutputPortByName("m_OpaqueColorPyramid_Out")
                ?? pyramidNode.GetOutputPortByName("m_OpaqueColorPyramid");
            IPort opaqueDepthOut = pyramidNode.GetOutputPortByName("m_OpaqueDepth_Out")
                ?? pyramidNode.GetOutputPortByName("m_OpaqueDepth");
            IPort backfaceOut = backfaceNode.GetOutputPortByName("m_BackfaceData_Out")
                ?? backfaceNode.GetOutputPortByName("m_BackfaceData");

            changed |= ConnectIfMissing(lightingScope, pyramidOut, refractNode.GetInputPortByName("m_OpaqueColorPyramid"));
            changed |= ConnectIfMissing(lightingScope, opaqueDepthOut, refractNode.GetInputPortByName("m_OpaqueDepth"));
            changed |= ConnectIfMissing(lightingScope, backfaceOut, refractNode.GetInputPortByName("m_BackfaceData"));

            if (shadowSource != null)
                changed |= ConnectIfMissing(lightingScope, shadowSource, refractNode.GetInputPortByName("m_DirectionalShadowTexture"));

            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_DirectionalLightBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_PunctualLightBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_AreaLightBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_ReflectionProbeBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_LayeredOffsetBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_LayeredLightListBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, refractNode, "m_LogBaseBuffer", ref changed);

            if (changed)
            {
                GraphDatabase.SaveGraph(graph);
                AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
            }

            return changed;
        }

        private static void ConnectLightPort(
            RenderGraphEditorGraph scope,
            RenderPassNodeData deferred,
            RenderPassNodeData lightGrid,
            RenderPassNodeData targetNode,
            string fieldName,
            ref bool changed)
        {
            IPort source = deferred.GetInputPortByName(fieldName)?.FirstConnectedPort
                ?? lightGrid?.GetOutputPortByName(fieldName)
                ?? lightGrid?.GetOutputPortByName($"{fieldName}_Out");
            if (source == null)
                return;

            changed |= ConnectIfMissing(scope, source, targetNode.GetInputPortByName(fieldName));
        }

        private static bool ConnectIfMissing(RenderGraphEditorGraph scope, IPort from, IPort to)
        {
            if (from == null || to == null || to.IsConnected)
                return false;

            if (!scope.Connect(from, to))
                throw new InvalidOperationException($"Failed to connect '{from.GetNode()}.{from.Name}' -> '{to.GetNode()}.{to.Name}'.");

            return true;
        }

        private static RenderPassNodeData CreatePassNode(RenderGraphEditorGraph scope, Type passType, Vector2 position)
        {
            var nodeType = RenderPassNodeRegistry.GetNodeType(passType)
                ?? throw new InvalidOperationException($"No generated RenderGraph node registered for pass type '{passType.FullName}'.");
            var node = (RenderPassNodeData)Activator.CreateInstance(nodeType);
            node.Position = position;
            scope.AddNode(node);
            return node;
        }

        private static List<RenderGraphEditorGraph> CollectOwnedScopes(RenderGraphEditorGraph root, string graphPath)
        {
            var result = new List<RenderGraphEditorGraph> { root };
            var queue = new Queue<RenderGraphEditorGraph>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var sgNode in current.GetNodes().OfType<ISubgraphNode>())
                {
                    if (sgNode.GetSubgraph() is RenderGraphEditorGraph child
                        && string.Equals(GraphDatabase.GetGraphAssetPath(child), graphPath, StringComparison.OrdinalIgnoreCase)
                        && !result.Contains(child))
                    {
                        result.Add(child);
                        queue.Enqueue(child);
                    }
                }
            }

            return result;
        }
    }
}
