using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderGraph.Data
{
    public struct GraphValidationResult
    {
        public bool IsValid;
        public List<string> Errors;
        public List<string> TopologicalOrder;
    }

    [CreateAssetMenu(menuName = "VividRP/Render Graph")]
    public class RenderGraphAsset : ScriptableObject
    {
        [SerializeReference] public List<RenderGraphNodeData> Nodes = new List<RenderGraphNodeData>();
        public List<RenderGraphEdgeData> Edges = new List<RenderGraphEdgeData>();

        public void AddNode(RenderGraphNodeData node)
        {
            Nodes.Add(node);
        }

        public void RemoveNode(string guid)
        {
            if (Nodes != null)
            {
                foreach (var node in Nodes)
                {
                    if (node.Guid == guid && node is PreviewPassNodeData previewNode)
                    {
                        PreviewPassNodeData.ReleasePreviewResources(previewNode.Guid);
                        break;
                    }
                }
            }

            Nodes.RemoveAll(n => n.Guid == guid);
            Edges.RemoveAll(e => e.OutputNodeGuid == guid || e.InputNodeGuid == guid);
        }

        public void AddEdge(RenderGraphEdgeData edge)
        {
            Edges.Add(edge);
        }

        public void RemoveEdge(string outputNodeGuid, string outputPortId, string inputNodeGuid, string inputPortId)
        {
            Edges.RemoveAll(e =>
                e.OutputNodeGuid == outputNodeGuid &&
                e.OutputPortId == outputPortId &&
                e.InputNodeGuid == inputNodeGuid &&
                e.InputPortId == inputPortId);
        }

        /// <summary>
        /// Validates the graph is a DAG with no cycles using Kahn's algorithm.
        /// Returns topological order on success, or cycle participant names on failure.
        /// </summary>
        public GraphValidationResult Validate()
        {
            var result = new GraphValidationResult { Errors = new List<string>() };

            if (Nodes == null || Nodes.Count == 0)
            {
                result.IsValid = true;
                result.TopologicalOrder = new List<string>();
                return result;
            }

            // Build adjacency + in-degree from edges (output → input)
            var inDegree = new Dictionary<string, int>();
            var adjacency = new Dictionary<string, List<string>>();
            var nameMap = new Dictionary<string, string>();

            foreach (var node in Nodes)
            {
                inDegree[node.Guid] = 0;
                adjacency[node.Guid] = new List<string>();
                nameMap[node.Guid] = node.NodeName ?? node.Guid;
            }

            foreach (var edge in Edges)
            {
                if (!adjacency.ContainsKey(edge.OutputNodeGuid) ||
                    !inDegree.ContainsKey(edge.InputNodeGuid))
                    continue;

                adjacency[edge.OutputNodeGuid].Add(edge.InputNodeGuid);
                inDegree[edge.InputNodeGuid]++;
            }

            // Kahn's: seed queue with zero in-degree nodes
            var queue = new Queue<string>();
            foreach (var kv in inDegree)
            {
                if (kv.Value == 0)
                    queue.Enqueue(kv.Key);
            }

            var sorted = new List<string>();
            while (queue.Count > 0)
            {
                var guid = queue.Dequeue();
                sorted.Add(guid);

                foreach (var neighbor in adjacency[guid])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }

            if (sorted.Count == Nodes.Count)
            {
                result.IsValid = true;
                result.TopologicalOrder = sorted;
            }
            else
            {
                result.IsValid = false;
                // Nodes not in sorted list are part of cycle(s)
                var inCycle = new HashSet<string>();
                foreach (var node in Nodes)
                {
                    if (!sorted.Contains(node.Guid))
                        inCycle.Add(node.Guid);
                }

                result.Errors.Add("Graph contains a cycle involving: " +
                    string.Join(", ", CycleNodeNames(inCycle, nameMap)));
            }

            return result;
        }

        /// <summary>
        /// Returns true if adding an edge from outputNodeGuid to inputNodeGuid would create a cycle.
        /// </summary>
        public bool WouldCreateCycle(string outputNodeGuid, string inputNodeGuid)
        {
            if (outputNodeGuid == inputNodeGuid)
                return true;

            // DFS from inputNodeGuid following existing edges — if we can reach outputNodeGuid, it's a cycle
            var visited = new HashSet<string>();
            var stack = new Stack<string>();
            stack.Push(inputNodeGuid);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == outputNodeGuid)
                    return true;

                if (!visited.Add(current))
                    continue;

                foreach (var edge in Edges)
                {
                    if (edge.OutputNodeGuid == current)
                        stack.Push(edge.InputNodeGuid);
                }
            }

            return false;
        }

        private static IEnumerable<string> CycleNodeNames(
            HashSet<string> guids, Dictionary<string, string> nameMap)
        {
            foreach (var guid in guids)
                yield return nameMap.TryGetValue(guid, out var name) ? name : guid;
        }
    }
}
