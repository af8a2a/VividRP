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
    /// GraphToolkit utility to wire the 3 standard Strand Hair RenderGraph passes
    /// (StrandHairVisibilityPass -> StrandHairShadingPass -> StrandHairCompositionPass)
    /// into a VividRP .vrdg graph without any runtime RenderGraph hooks.
    /// </summary>
    public static class StrandHairRenderGraphUtility
    {
        public static bool AddStrandHairPasses(string graphPath)
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
            var lightGrid = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(LightGridPass));
            var hzb = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(HZBGeneratePass));

            var existingVis = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(StrandHairVisibilityPass));
            var existingShade = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(StrandHairShadingPass));

            bool changed = false;

            // Resolve Depth, HZB, LightGrid, and CSMShadowAtlas ports inside the Lighting scope
            IPort depthSource = deferred.GetInputPortByName("m_DepthTexture")?.FirstConnectedPort;
            if (depthSource == null)
                throw new InvalidOperationException($"DeferredLightingPass.m_DepthTexture is not connected in '{graphPath}'.");

            IPort hzbSource = hzb?.GetOutputPortByName("m_HzbTexture")
                ?? FindVariableOutputInScope(lightingScope, "HZB");

            IPort csmSource = FindVariableOutputInScope(lightingScope, "CSMShadowAtlas");

            // 1. StrandHairVisibilityPass (Async Compute in Lighting scope)
            var visNode = existingVis;
            if (visNode == null)
            {
                visNode = CreatePassNode(lightingScope, typeof(StrandHairVisibilityPass), deferred.Position + new Vector2(-380f, 620f));
                changed = true;
            }
            visNode.DefineNode();
            changed |= SetEnableAsyncCompute(visNode, true);
            changed |= ConnectIfMissing(lightingScope, depthSource, visNode.GetInputPortByName("m_DepthTexture"));
            if (hzbSource != null)
                changed |= ConnectIfMissing(lightingScope, hzbSource, visNode.GetInputPortByName("m_HzbTexture"));

            // 2. StrandHairShadingPass (Async Compute in Lighting scope, directly wired to LightGrid + CSM)
            var shadeNode = existingShade;
            if (shadeNode == null)
            {
                shadeNode = CreatePassNode(lightingScope, typeof(StrandHairShadingPass), deferred.Position + new Vector2(0f, 620f));
                changed = true;
            }
            shadeNode.DefineNode();
            changed |= SetEnableAsyncCompute(shadeNode, true);
            changed |= ConnectIfMissing(lightingScope, visNode.GetOutputPortByName("m_VisAtlasBuffer"), shadeNode.GetInputPortByName("m_VisAtlasBuffer"));

            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_DirectionalLightBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_PunctualLightBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_AreaLightBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_ReflectionProbeBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_LayeredOffsetBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_LayeredLightListBuffer", ref changed);
            ConnectLightPort(lightingScope, deferred, lightGrid, shadeNode, "m_LogBaseBuffer", ref changed);
            if (csmSource != null)
                changed |= ConnectIfMissing(lightingScope, csmSource, shadeNode.GetInputPortByName("m_CSMShadowAtlas"));

            // 3. StrandHairCompositionPass: place after SkyInjectionPass if in same/root scope, else right after DeferredLightingPass
            var skyScope = scopes.FirstOrDefault(s =>
                s.GetNodes().OfType<RenderPassNodeData>().Any(n => n.GetPassType() == typeof(SkyInjectionPass)));

            if (skyScope == null || ReferenceEquals(skyScope, lightingScope))
            {
                var compTarget = skyScope != null
                    ? lightingNodes.First(n => n.GetPassType() == typeof(SkyInjectionPass))
                    : deferred;
                var colorOut = compTarget.GetOutputPortByName("m_ColorTexture_Out")
                    ?? compTarget.GetOutputPortByName("m_ColorTexture");
                var existingComp = lightingNodes.FirstOrDefault(n => n.GetPassType() == typeof(StrandHairCompositionPass));
                changed |= WireCompositionInScope(
                    lightingScope,
                    existingComp,
                    colorOut,
                    visNode.GetOutputPortByName("m_VisAtlasBuffer"),
                    shadeNode.GetOutputPortByName("m_ShadingSyncBuffer"),
                    compTarget.Position + new Vector2(360f, 0f));
            }
            else
            {
                // SkyInjectionPass lives in a separate scope (typically root or Sky subgraph):
                // expose HairVisAtlas and HairShadingSync as outputs of Lighting subgraph and wire Composition after SkyInjectionPass.
                ISubgraphNode lightingSubgraphNode = FindSubgraphNode(graph, lightingScope);
                if (lightingSubgraphNode == null)
                    throw new InvalidOperationException($"Could not locate SubGraphNode owning Lighting scope in '{graphPath}'.");

                IPort visAtlasRootOut = ExposeBufferOutputFromSubgraph(
                    graph,
                    lightingSubgraphNode,
                    lightingScope,
                    "HairVisAtlas (W)",
                    visNode.GetOutputPortByName("m_VisAtlasBuffer"),
                    visNode.Position + new Vector2(320f, 180f),
                    ref changed);

                IPort shadingSyncRootOut = ExposeBufferOutputFromSubgraph(
                    graph,
                    lightingSubgraphNode,
                    lightingScope,
                    "HairShadingSync (W)",
                    shadeNode.GetOutputPortByName("m_ShadingSyncBuffer"),
                    shadeNode.Position + new Vector2(320f, 180f),
                    ref changed);

                if (ReferenceEquals(skyScope, graph))
                {
                    var rootNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
                    var skyPass = rootNodes.First(n => n.GetPassType() == typeof(SkyInjectionPass));
                    var colorOut = skyPass.GetOutputPortByName("m_ColorTexture_Out")
                        ?? skyPass.GetOutputPortByName("m_ColorTexture");
                    var existingComp = rootNodes.FirstOrDefault(n => n.GetPassType() == typeof(StrandHairCompositionPass));
                    changed |= WireCompositionInScope(
                        graph,
                        existingComp,
                        colorOut,
                        visAtlasRootOut,
                        shadingSyncRootOut,
                        skyPass.Position + new Vector2(360f, 0f));
                }
                else
                {
                    // SkyInjectionPass is inside a sibling subgraph (e.g. "Sky"): wire Composition in root right after that subgraph's Color output.
                    ISubgraphNode skySubgraphNode = FindSubgraphNode(graph, skyScope);
                    if (skySubgraphNode == null)
                        throw new InvalidOperationException($"Could not locate SubGraphNode owning SkyInjectionPass in '{graphPath}'.");

                    IPort skyColorOut = skySubgraphNode.GetOutputPorts().FirstOrDefault(p =>
                        p.Name.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0)
                        ?? skySubgraphNode.GetOutputPorts().FirstOrDefault();

                    var rootNodes = graph.GetNodes().OfType<RenderPassNodeData>().ToArray();
                    var existingComp = rootNodes.FirstOrDefault(n => n.GetPassType() == typeof(StrandHairCompositionPass));
                    changed |= WireCompositionInScope(
                        graph,
                        existingComp,
                        skyColorOut,
                        visAtlasRootOut,
                        shadingSyncRootOut,
                        skySubgraphNode.Position + new Vector2(360f, 0f));
                }
            }

            if (changed)
            {
                GraphDatabase.SaveGraph(graph);
                AssetDatabase.ImportAsset(graphPath, ImportAssetOptions.ForceSynchronousImport);
            }

            return changed;
        }

        private static bool WireCompositionInScope(
            RenderGraphEditorGraph scope,
            RenderPassNodeData existingComp,
            IPort colorProducerOut,
            IPort visAtlasOut,
            IPort shadingSyncOut,
            Vector2 position)
        {
            if (colorProducerOut == null)
                throw new InvalidOperationException("Could not resolve SceneColor output port for StrandHairCompositionPass.");

            bool changed = false;
            var compNode = existingComp;
            if (compNode == null)
            {
                compNode = CreatePassNode(scope, typeof(StrandHairCompositionPass), position);
                changed = true;
            }
            compNode.DefineNode();

            IPort compColorIn = compNode.GetInputPortByName("m_ColorTexture_In")
                ?? compNode.GetInputPortByName("m_ColorTexture");
            IPort compColorOut = compNode.GetOutputPortByName("m_ColorTexture_Out")
                ?? compNode.GetOutputPortByName("m_ColorTexture");

            // Splice StrandHairCompositionPass into SceneColor chain:
            // capture existing consumers of colorProducerOut (excluding compNode itself) and rewire them to compColorOut.
            var downstreamInputs = new List<IPort>();
            colorProducerOut.GetConnectedPorts(downstreamInputs);
            bool alreadySpliced = downstreamInputs.Any(p => ReferenceEquals(p.GetNode(), compNode));

            if (!alreadySpliced)
            {
                foreach (var consumer in downstreamInputs)
                {
                    if (ReferenceEquals(consumer.GetNode(), compNode))
                        continue;
                    scope.Disconnect(colorProducerOut, consumer);
                    if (!scope.Connect(compColorOut, consumer))
                        throw new InvalidOperationException($"Failed to rewire SceneColor consumer '{consumer.GetNode()}.{consumer.Name}' to StrandHairCompositionPass.");
                }

                if (!scope.Connect(colorProducerOut, compColorIn))
                    throw new InvalidOperationException("Failed to connect SceneColor into StrandHairCompositionPass.");
                changed = true;
            }

            changed |= ConnectIfMissing(scope, visAtlasOut, compNode.GetInputPortByName("m_VisAtlasBuffer"));
            changed |= ConnectIfMissing(scope, shadingSyncOut, compNode.GetInputPortByName("m_ShadingSyncBuffer"));
            return changed;
        }

        private static IPort ExposeBufferOutputFromSubgraph(
            RenderGraphEditorGraph rootGraph,
            ISubgraphNode subgraphNode,
            RenderGraphEditorGraph subgraph,
            string variableName,
            IPort internalProducerPort,
            Vector2 variablePos,
            ref bool changed)
        {
            var existingVar = subgraph.GetVariables().FirstOrDefault(v =>
                v.VariableKind == VariableKind.Output
                && string.Equals(v.Name, variableName, StringComparison.Ordinal));

            if (existingVar == null)
            {
                existingVar = subgraph.CreateVariable(
                    variableName,
                    typeof(RenderGraphBuffer),
                    RenderGraphBuffer.CreateStructured(variableName, 1, sizeof(uint)),
                    VariableKind.Local);
                existingVar.VariableKind = VariableKind.Output;
                subgraph.AddVariableNode(existingVar, variablePos);
                changed = true;
            }

            var varNodes = new List<IVariableNode>();
            existingVar.GetNodes(varNodes);
            if (varNodes.Count == 0)
            {
                subgraph.AddVariableNode(existingVar, variablePos);
                existingVar.GetNodes(varNodes);
                changed = true;
            }

            var varInPort = varNodes[0].GetInputPort(0);
            changed |= ConnectIfMissing(subgraph, internalProducerPort, varInPort);

            RenderGraphSubSystemReflectionUtility.TryRefreshPorts(subgraphNode);
            if (!RenderGraphSubSystemReflectionUtility.TryGetOutputPortForVariable(subgraphNode, existingVar, out var outPort)
                || outPort == null)
            {
                throw new InvalidOperationException($"Could not resolve SubGraph output port for variable '{variableName}'.");
            }

            return outPort;
        }

        private static void ConnectLightPort(
            RenderGraphEditorGraph scope,
            RenderPassNodeData deferred,
            RenderPassNodeData lightGrid,
            RenderPassNodeData shadeNode,
            string fieldName,
            ref bool changed)
        {
            IPort source = deferred.GetInputPortByName(fieldName)?.FirstConnectedPort
                ?? lightGrid?.GetOutputPortByName(fieldName)
                ?? lightGrid?.GetOutputPortByName($"{fieldName}_Out");
            if (source == null)
                return;

            changed |= ConnectIfMissing(scope, source, shadeNode.GetInputPortByName(fieldName));
        }

        private static IPort FindVariableOutputInScope(RenderGraphEditorGraph scope, string containsName)
        {
            foreach (var v in scope.GetVariables())
            {
                if (v.VariableKind != VariableKind.Input)
                    continue;
                if (v.Name.IndexOf(containsName, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var nodes = new List<IVariableNode>();
                v.GetNodes(nodes);
                if (nodes.Count > 0 && nodes[0].OutputPortCount > 0)
                    return nodes[0].GetOutputPort(0);
            }

            return null;
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

        private static bool SetEnableAsyncCompute(RenderPassNodeData node, bool enabled)
        {
            var opt = node.GetNodeOptionByName("AsyncCompute");
            if (opt == null)
                return false;
            if (opt.TryGetValue<bool>(out var current) && current == enabled)
                return false;
            return opt.TrySetValue(enabled);
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

        private static ISubgraphNode FindSubgraphNode(RenderGraphEditorGraph root, RenderGraphEditorGraph target)
        {
            foreach (var sgNode in root.GetNodes().OfType<ISubgraphNode>())
            {
                if (ReferenceEquals(sgNode.GetSubgraph(), target))
                    return sgNode;
            }

            return null;
        }
    }
}
