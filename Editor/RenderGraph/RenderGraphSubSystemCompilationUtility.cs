using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;

namespace VividRP.Editor.RenderGraph
{
    internal static class RenderGraphSubSystemCompilationUtility
    {
        internal static RenderGraphFlattenedGraph Flatten(RenderGraphEditorGraph graph)
        {
            return RenderGraphFlattenedGraph.Create(graph);
        }

        internal static bool IsSubSystemGraph(RenderGraphEditorGraph graph)
        {
            return graph is RenderGraphSubSystemGraph;
        }

        internal static IPort ResolveInputConnection(RenderGraphFlattenedGraph flattenedGraph, INode currentNode, IPort connectedPort)
        {
            if (flattenedGraph == null || currentNode == null)
                return connectedPort;

            return ResolveInputConnection(flattenedGraph, flattenedGraph.GetScope(currentNode), connectedPort);
        }

        internal static IPort ResolveOutputConnection(RenderGraphFlattenedGraph flattenedGraph, INode currentNode, IPort connectedPort)
        {
            if (flattenedGraph == null || currentNode == null)
                return connectedPort;

            return ResolveOutputConnection(flattenedGraph, flattenedGraph.GetScope(currentNode), connectedPort);
        }

        private static IPort ResolveInputConnection(
            RenderGraphFlattenedGraph flattenedGraph,
            RenderGraphCompilationScope scope,
            IPort connectedPort)
        {
            if (flattenedGraph == null || connectedPort == null)
                return null;

            var connectedNode = connectedPort.GetNode();
            if (connectedNode is ISubgraphNode subgraphNode)
                return ResolveSubgraphOutput(flattenedGraph, subgraphNode, connectedPort);

            if (connectedNode is IVariableNode variableNode)
                return ResolveVariableSource(flattenedGraph, scope, variableNode.Variable);

            return connectedPort;
        }

        private static IPort ResolveOutputConnection(
            RenderGraphFlattenedGraph flattenedGraph,
            RenderGraphCompilationScope scope,
            IPort connectedPort)
        {
            if (flattenedGraph == null || connectedPort == null)
                return null;

            return connectedPort.GetNode() switch
            {
                ISubgraphNode => null,
                IVariableNode => null,
                _ => connectedPort,
            };
        }

        private static IPort ResolveVariableSource(
            RenderGraphFlattenedGraph flattenedGraph,
            RenderGraphCompilationScope scope,
            IVariable variable)
        {
            if (flattenedGraph == null || variable == null)
                return null;

            return variable.VariableKind switch
            {
                VariableKind.Input => ResolveInputVariableSource(flattenedGraph, scope, variable),
                VariableKind.Output => ResolveOutputVariableSource(flattenedGraph, scope, variable),
                _ => null,
            };
        }

        private static IPort ResolveInputVariableSource(
            RenderGraphFlattenedGraph flattenedGraph,
            RenderGraphCompilationScope scope,
            IVariable variable)
        {
            if (scope == null || scope.Parent == null || scope.OwnerSubgraphNode == null || variable == null)
                return null;

            if (scope.TryGetCachedInput(variable, out var cached))
                return cached;

            IPort resolvedPort = null;
            if (RenderGraphSubSystemReflectionUtility.TryGetInputPortForVariable(scope.OwnerSubgraphNode, variable, out var inputPort))
            {
                resolvedPort = ResolveInputConnection(flattenedGraph, scope.Parent, inputPort?.FirstConnectedPort);
            }

            scope.CacheInput(variable, resolvedPort);
            return resolvedPort;
        }

        private static IPort ResolveOutputVariableSource(
            RenderGraphFlattenedGraph flattenedGraph,
            RenderGraphCompilationScope scope,
            IVariable variable)
        {
            if (scope == null || variable == null)
                return null;

            if (scope.TryGetCachedOutput(variable, out var cached))
                return cached;

            var variableNodes = new List<IVariableNode>();
            variable.GetNodes(variableNodes);

            var resolvedPorts = new HashSet<IPort>(ReferenceEqualityComparer<IPort>.Instance);
            foreach (var variableNode in variableNodes)
            {
                foreach (var inputPort in variableNode.GetInputPorts())
                {
                    if (inputPort?.IsConnected != true)
                        continue;

                    var resolvedPort = ResolveInputConnection(flattenedGraph, scope, inputPort.FirstConnectedPort);
                    if (resolvedPort != null)
                        resolvedPorts.Add(resolvedPort);
                }
            }

            var resolved = resolvedPorts.Count == 1 ? resolvedPorts.First() : null;
            scope.CacheOutput(variable, resolved);
            return resolved;
        }

        private static IPort ResolveSubgraphOutput(
            RenderGraphFlattenedGraph flattenedGraph,
            ISubgraphNode subgraphNode,
            IPort outputPort)
        {
            if (flattenedGraph == null || subgraphNode == null || outputPort == null)
                return null;

            if (!flattenedGraph.TryGetChildScope(subgraphNode, out var childScope))
                return null;

            if (!RenderGraphSubSystemReflectionUtility.TryGetVariableForOutputPort(subgraphNode, outputPort, out var variable))
                return null;

            return ResolveOutputVariableSource(flattenedGraph, childScope, variable);
        }
    }

    internal sealed class RenderGraphFlattenedGraph
    {
        private readonly Dictionary<INode, RenderGraphCompilationScope> m_NodeScopes = new(ReferenceEqualityComparer<INode>.Instance);
        private readonly Dictionary<ISubgraphNode, RenderGraphCompilationScope> m_ChildScopes = new(ReferenceEqualityComparer<ISubgraphNode>.Instance);

        private RenderGraphFlattenedGraph(RenderGraphCompilationScope rootScope)
        {
            RootScope = rootScope;
        }

        internal RenderGraphCompilationScope RootScope { get; }
        internal List<RenderPassNodeData> PassNodes { get; } = new();
        internal List<TextureResourceNodeData> TextureNodes { get; } = new();
        internal List<HistoryResourceNodeData> HistoryNodes { get; } = new();
        internal List<BufferResourceNodeData> BufferNodes { get; } = new();
        internal List<RenderListResourceNodeData> RenderListNodes { get; } = new();
        internal List<AccelerationStructureResourceNodeData> AccelerationStructureNodes { get; } = new();

        internal RenderGraphCompilationScope GetScope(INode node)
        {
            return node != null && m_NodeScopes.TryGetValue(node, out var scope)
                ? scope
                : RootScope;
        }

        internal bool TryGetChildScope(ISubgraphNode node, out RenderGraphCompilationScope scope)
        {
            scope = null;
            return node != null && m_ChildScopes.TryGetValue(node, out scope);
        }

        internal static RenderGraphFlattenedGraph Create(RenderGraphEditorGraph graph)
        {
            if (graph == null)
                return new RenderGraphFlattenedGraph(null);

            var flattenedGraph = new RenderGraphFlattenedGraph(new RenderGraphCompilationScope(graph));
            Collect(flattenedGraph, flattenedGraph.RootScope);
            return flattenedGraph;
        }

        private static void Collect(RenderGraphFlattenedGraph flattenedGraph, RenderGraphCompilationScope scope)
        {
            if (flattenedGraph == null || scope?.Graph == null)
                return;

            foreach (var node in scope.Graph.GetNodes())
            {
                if (node == null)
                    continue;

                switch (node)
                {
                    case RenderPassNodeData passNode:
                        flattenedGraph.PassNodes.Add(passNode);
                        flattenedGraph.m_NodeScopes[passNode] = scope;
                        break;
                    case TextureResourceNodeData textureNode:
                        flattenedGraph.TextureNodes.Add(textureNode);
                        flattenedGraph.m_NodeScopes[textureNode] = scope;
                        break;
                    case HistoryResourceNodeData historyNode:
                        flattenedGraph.HistoryNodes.Add(historyNode);
                        flattenedGraph.m_NodeScopes[historyNode] = scope;
                        break;
                    case BufferResourceNodeData bufferNode:
                        flattenedGraph.BufferNodes.Add(bufferNode);
                        flattenedGraph.m_NodeScopes[bufferNode] = scope;
                        break;
                    case RenderListResourceNodeData renderListNode:
                        flattenedGraph.RenderListNodes.Add(renderListNode);
                        flattenedGraph.m_NodeScopes[renderListNode] = scope;
                        break;
                    case AccelerationStructureResourceNodeData accelerationStructureNode:
                        flattenedGraph.AccelerationStructureNodes.Add(accelerationStructureNode);
                        flattenedGraph.m_NodeScopes[accelerationStructureNode] = scope;
                        break;
                    case ISubgraphNode subgraphNode when scope.Parent == null && !RenderGraphSubSystemCompilationUtility.IsSubSystemGraph(scope.Graph):
                        if (subgraphNode.GetSubgraph() is RenderGraphEditorGraph childGraph)
                        {
                            var childScope = new RenderGraphCompilationScope(childGraph, scope, subgraphNode);
                            flattenedGraph.m_ChildScopes[subgraphNode] = childScope;
                            Collect(flattenedGraph, childScope);
                        }
                        break;
                }
            }
        }
    }

    internal sealed class RenderGraphCompilationScope
    {
        private readonly Dictionary<IVariable, IPort> m_InputCache = new(ReferenceEqualityComparer<IVariable>.Instance);
        private readonly HashSet<IVariable> m_CachedInputs = new(ReferenceEqualityComparer<IVariable>.Instance);
        private readonly Dictionary<IVariable, IPort> m_OutputCache = new(ReferenceEqualityComparer<IVariable>.Instance);
        private readonly HashSet<IVariable> m_CachedOutputs = new(ReferenceEqualityComparer<IVariable>.Instance);

        internal RenderGraphCompilationScope(
            RenderGraphEditorGraph graph,
            RenderGraphCompilationScope parent = null,
            ISubgraphNode ownerSubgraphNode = null)
        {
            Graph = graph;
            Parent = parent;
            OwnerSubgraphNode = ownerSubgraphNode;
        }

        internal RenderGraphEditorGraph Graph { get; }
        internal RenderGraphCompilationScope Parent { get; }
        internal ISubgraphNode OwnerSubgraphNode { get; }

        internal bool TryGetCachedInput(IVariable variable, out IPort port)
        {
            port = null;
            return variable != null
                   && m_CachedInputs.Contains(variable)
                   && m_InputCache.TryGetValue(variable, out port);
        }

        internal void CacheInput(IVariable variable, IPort port)
        {
            if (variable == null)
                return;

            m_CachedInputs.Add(variable);
            m_InputCache[variable] = port;
        }

        internal bool TryGetCachedOutput(IVariable variable, out IPort port)
        {
            port = null;
            return variable != null
                   && m_CachedOutputs.Contains(variable)
                   && m_OutputCache.TryGetValue(variable, out port);
        }

        internal void CacheOutput(IVariable variable, IPort port)
        {
            if (variable == null)
                return;

            m_CachedOutputs.Add(variable);
            m_OutputCache[variable] = port;
        }
    }
}
