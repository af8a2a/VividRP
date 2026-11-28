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
    /// GraphToolkit utility to splice the standard SubsurfaceScatteringPass node
    /// directly after DeferredLightingPass in any VividRP .vrdg graph.
    /// </summary>
    public static class SubsurfaceScatteringRenderGraphUtility
    {
        public static bool AddSubsurfaceScatteringPass(string graphPath)
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
            deferred.DefineNode();

            var sssNode = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(SubsurfaceScatteringPass));
            bool changed = false;
            if (sssNode == null)
            {
                var nodeType = RenderPassNodeRegistry.GetNodeType(typeof(SubsurfaceScatteringPass))
                    ?? throw new InvalidOperationException($"No generated RenderGraph node registered for '{nameof(SubsurfaceScatteringPass)}'.");
                sssNode = (RenderPassNodeData)Activator.CreateInstance(nodeType);
                sssNode.Position = deferred.Position + new Vector2(360f, 140f);
                lightingScope.AddNode(sssNode);
                changed = true;
            }

            sssNode.DefineNode();

            IPort deferredColorOut = deferred.GetOutputPortByName("m_ColorTexture_Out")
                ?? deferred.GetOutputPortByName("m_ColorTexture");
            IPort deferredSssOut = deferred.GetOutputPortByName("m_SubsurfaceDiffuse_Out")
                ?? deferred.GetOutputPortByName("m_SubsurfaceDiffuse");
            IPort sssColorIn = sssNode.GetInputPortByName("m_ColorTexture_In")
                ?? sssNode.GetInputPortByName("m_ColorTexture");
            IPort sssColorOut = sssNode.GetOutputPortByName("m_ColorTexture_Out")
                ?? sssNode.GetOutputPortByName("m_ColorTexture");
            IPort sssDiffuseIn = sssNode.GetInputPortByName("m_SubsurfaceDiffuse");

            if (deferredColorOut == null || deferredSssOut == null || sssColorIn == null || sssColorOut == null || sssDiffuseIn == null)
                throw new InvalidOperationException($"Failed to resolve SubsurfaceScatteringPass / DeferredLightingPass ports in '{graphPath}'.");

            var downstreamConsumers = new List<IPort>();
            deferredColorOut.GetConnectedPorts(downstreamConsumers);
            bool alreadySpliced = downstreamConsumers.Any(p => ReferenceEquals(p.GetNode(), sssNode));

            if (!alreadySpliced)
            {
                foreach (var consumer in downstreamConsumers)
                {
                    if (ReferenceEquals(consumer.GetNode(), sssNode))
                        continue;
                    lightingScope.Disconnect(deferredColorOut, consumer);
                    if (!lightingScope.Connect(sssColorOut, consumer))
                        throw new InvalidOperationException($"Failed to rewire SceneColor consumer '{consumer.GetNode()}.{consumer.Name}' to SubsurfaceScatteringPass.");
                }

                if (!lightingScope.Connect(deferredColorOut, sssColorIn))
                    throw new InvalidOperationException("Failed to connect DeferredLightingPass Color -> SubsurfaceScatteringPass.");
                changed = true;
            }

            if (!sssDiffuseIn.IsConnected)
            {
                if (!lightingScope.Connect(deferredSssOut, sssDiffuseIn))
                    throw new InvalidOperationException("Failed to connect DeferredLightingPass SubsurfaceDiffuse -> SubsurfaceScatteringPass.");
                changed = true;
            }

            if (changed)
            {
                GraphDatabase.SaveGraph(graph);
                AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
            }

            return changed;
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
