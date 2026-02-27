using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderGraph.Data
{
    public struct GraphValidationResult
    {
        public bool IsValid;
        public List<string> Errors;
        public List<string> Warnings;
        public List<string> TopologicalOrder;
    }

    [CreateAssetMenu(menuName = "VividRP/Render Graph")]
    public class RenderGraphAsset : ScriptableObject
    {
        [SerializeReference] public List<RenderGraphNodeData> Nodes = new List<RenderGraphNodeData>();
        public List<RenderGraphEdgeData> Edges = new List<RenderGraphEdgeData>();

        /// <summary>
        /// Monotonically increasing version counter. Incremented on any structural mutation.
        /// Used by the executor to detect when recompilation is needed.
        /// </summary>
        [System.NonSerialized] public int Version;

        public void AddNode(RenderGraphNodeData node)
        {
            Nodes.Add(node);
            Version++;
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
            Version++;
        }

        public void AddEdge(RenderGraphEdgeData edge)
        {
            Edges.Add(edge);
            Version++;
        }

        public void RemoveEdge(string outputNodeGuid, string outputPortId, string inputNodeGuid, string inputPortId)
        {
            Edges.RemoveAll(e =>
                e.OutputNodeGuid == outputNodeGuid &&
                e.OutputPortId == outputPortId &&
                e.InputNodeGuid == inputNodeGuid &&
                e.InputPortId == inputPortId);
            Version++;
        }

        /// <summary>
        /// Validates the graph is a DAG with no cycles using Kahn's algorithm.
        /// Returns topological order on success, or cycle participant names on failure.
        /// </summary>
        public GraphValidationResult Validate()
        {
            var result = new GraphValidationResult
            {
                Errors = new List<string>(),
                Warnings = new List<string>()
            };

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

            ValidateRasterPasses(ref result);
            if (result.Errors.Count > 0)
                result.IsValid = false;

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

        private void ValidateRasterPasses(ref GraphValidationResult result)
        {
            if (Nodes == null || Nodes.Count == 0)
                return;

            var nodeMap = new Dictionary<string, RenderGraphNodeData>();
            foreach (var node in Nodes)
                nodeMap[node.Guid] = node;

            foreach (var node in Nodes)
            {
                if (node is not RasterPassNodeData rasterPass)
                    continue;

                if (!rasterPass.TryCompileLayout(out var compileErrors))
                {
                    foreach (var error in compileErrors)
                    {
                        result.Errors.Add(
                            $"Raster pass '{node.NodeName}' compile error: {error}");
                    }
                    continue;
                }

                rasterPass.EnsureBakedDescriptor();

                var baked = rasterPass.BakedPass;
                int colorAttachmentCount = baked?.ColorAttachments?.Length ?? 0;
                if (colorAttachmentCount > RasterPassNodeData.MaxColorAttachments)
                {
                    result.Errors.Add(
                        $"Raster pass '{node.NodeName}' MRT overflow: {colorAttachmentCount} > {RasterPassNodeData.MaxColorAttachments}.");
                }

                if (rasterPass.HasDepthAttachment())
                    continue;

                if (baked == null || baked.RendererLists == null || baked.RendererLists.Length == 0)
                    continue;

                if (!RendererListRequiresDepthBuffer(rasterPass, baked, nodeMap))
                    continue;

                result.Warnings.Add(
                    $"Raster pass '{node.NodeName}' consumes a depth-writing renderer list but has no depth attachment.");
            }
        }

        private bool RendererListRequiresDepthBuffer(
            RasterPassNodeData rasterPass,
            BakedRasterPass bakedPass,
            Dictionary<string, RenderGraphNodeData> nodeMap)
        {
            if (Edges == null || Edges.Count == 0)
                return false;

            foreach (var rendererList in bakedPass.RendererLists)
            {
                foreach (var edge in Edges)
                {
                    if (edge.InputNodeGuid != rasterPass.Guid ||
                        edge.InputPortId != rendererList.InputPortId)
                    {
                        continue;
                    }

                    if (!nodeMap.TryGetValue(edge.OutputNodeGuid, out var sourceNode))
                        continue;

                    if (sourceNode is RendererFilterNodeData filterNode &&
                        filterNode.Settings.RequireDepthBuffer)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
