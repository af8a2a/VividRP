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
    /// GraphToolkit utility to splice the 4 standard Refraction & Caustics passes:
    ///   SubsurfaceScatteringPass / DeferredLightingPass
    ///     -> TransmissiveCausticsPass (colored volumetric glass shadow + focused caustics into SceneColor)
    ///     -> OpaqueColorPyramidPass + TransparentBackfacePass
    ///     -> ForwardRefractionPass
    /// into any VividRP .vrdg graph. Idempotent for both fresh and Phase 1 graphs.
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
            var baseColorProducer = sssNode ?? deferred;

            IPort baseColorOut = baseColorProducer.GetOutputPortByName("m_ColorTexture_Out")
                ?? baseColorProducer.GetOutputPortByName("m_ColorTexture");
            IPort depthSource = deferred.GetInputPortByName("m_DepthTexture")?.FirstConnectedPort;
            IPort gbuffer0Source = deferred.GetInputPortByName("m_GBuffer0")?.FirstConnectedPort;
            IPort gbuffer1Source = deferred.GetInputPortByName("m_GBuffer1")?.FirstConnectedPort;
            IPort shadowSource = deferred.GetInputPortByName("m_DirectionalShadowTexture")?.FirstConnectedPort;

            if (baseColorOut == null || depthSource == null)
                throw new InvalidOperationException($"Failed to resolve SceneColor or Depth source in '{graphPath}'.");

            bool changed = false;

            // 0. TransmissiveCausticsPass (spliced directly after SubsurfaceScatteringPass / DeferredLightingPass,
            //    so both opaque SceneColor and OpaqueColorPyramid see colored glass shadows & caustics!)
            var causticsNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(TransmissiveCausticsPass));
            if (causticsNode == null)
            {
                causticsNode = CreatePassNode(lightingScope, typeof(TransmissiveCausticsPass), baseColorProducer.Position + new Vector2(240f, -40f));
                changed = true;
            }
            causticsNode.DefineNode();

            IPort causticsColorIn = causticsNode.GetInputPortByName("m_ColorTexture_In")
                ?? causticsNode.GetInputPortByName("m_ColorTexture");
            IPort causticsColorOut = causticsNode.GetOutputPortByName("m_ColorTexture_Out")
                ?? causticsNode.GetOutputPortByName("m_ColorTexture");

            if (causticsColorIn == null || causticsColorOut == null)
                throw new InvalidOperationException($"Failed to resolve TransmissiveCausticsPass color ports in '{graphPath}'.");

            var baseConsumers = new List<IPort>();
            baseColorOut.GetConnectedPorts(baseConsumers);
            bool causticsSpliced = baseConsumers.Any(p => ReferenceEquals(p.GetNode(), causticsNode));
            if (!causticsSpliced)
            {
                foreach (var consumer in baseConsumers)
                {
                    if (ReferenceEquals(consumer.GetNode(), causticsNode))
                        continue;

                    lightingScope.Disconnect(baseColorOut, consumer);
                    if (!lightingScope.Connect(causticsColorOut, consumer))
                        throw new InvalidOperationException($"Failed to rewire SceneColor consumer '{consumer.GetNode()}.{consumer.Name}' to TransmissiveCausticsPass.");
                }

                if (!lightingScope.Connect(baseColorOut, causticsColorIn))
                    throw new InvalidOperationException("Failed to connect SceneColor -> TransmissiveCausticsPass.");
                changed = true;
            }

            changed |= ConnectIfMissing(lightingScope, depthSource, causticsNode.GetInputPortByName("m_DepthTexture"));
            if (gbuffer0Source != null)
                changed |= ConnectIfMissing(lightingScope, gbuffer0Source, causticsNode.GetInputPortByName("m_GBuffer0"));
            if (gbuffer1Source != null)
                changed |= ConnectIfMissing(lightingScope, gbuffer1Source, causticsNode.GetInputPortByName("m_GBuffer1"));

            // All subsequent refraction passes consume SceneColor AFTER TransmissiveCausticsPass
            IPort colorProducerOut = causticsColorOut;

            // 1. OpaqueColorPyramidPass
            var pyramidNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(OpaqueColorPyramidPass));
            if (pyramidNode == null)
            {
                pyramidNode = CreatePassNode(lightingScope, typeof(OpaqueColorPyramidPass), baseColorProducer.Position + new Vector2(480f, 260f));
                changed = true;
            }
            pyramidNode.DefineNode();
            changed |= ConnectIfMissing(lightingScope, colorProducerOut, pyramidNode.GetInputPortByName("m_ColorTexture"));
            changed |= ConnectIfMissing(lightingScope, depthSource, pyramidNode.GetInputPortByName("m_DepthTexture"));

            // 2. TransparentBackfacePass
            var backfaceNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(TransparentBackfacePass));
            if (backfaceNode == null)
            {
                backfaceNode = CreatePassNode(lightingScope, typeof(TransparentBackfacePass), baseColorProducer.Position + new Vector2(480f, 480f));
                changed = true;
            }
            backfaceNode.DefineNode();

            // 3. ForwardRefractionPass
            var refractNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(ForwardRefractionPass));
            if (refractNode == null)
            {
                refractNode = CreatePassNode(lightingScope, typeof(ForwardRefractionPass), baseColorProducer.Position + new Vector2(820f, 120f));
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
