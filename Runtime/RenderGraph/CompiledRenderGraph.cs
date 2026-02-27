using System.Collections.Generic;
using VividRP.Runtime.RenderGraph.Data;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph
{
    public class CompiledRenderGraph
    {
        public struct InputBinding
        {
            public string InputPortId;
            public string SourceOutputPortId;
        }

        public struct PassThroughBinding
        {
            public string OutputPortId;
            public string MatchedInputPortId;
        }

        public struct NodeEntry
        {
            public RenderGraphNodeData Node;
            public InputBinding[] InputBindings;
            public PassThroughBinding[] PassThroughBindings;
        }

        public bool IsValid;
        public List<string> Errors;
        public List<string> Warnings;
        public NodeEntry[] Entries;

        public static CompiledRenderGraph Compile(RenderGraphAsset asset)
        {
            var compiled = new CompiledRenderGraph();

            var validation = asset.Validate();
            compiled.Warnings = validation.Warnings;
            compiled.Errors = validation.Errors;
            compiled.IsValid = validation.IsValid;

            if (!validation.IsValid)
                return compiled;

            // Build port source map once
            var portSourceMap = new Dictionary<string, string>();
            foreach (var edge in asset.Edges)
                portSourceMap[edge.InputPortId] = edge.OutputPortId;

            // Build node map once
            var nodeMap = new Dictionary<string, RenderGraphNodeData>();
            foreach (var node in asset.Nodes)
                nodeMap[node.Guid] = node;

            var entries = new List<NodeEntry>(validation.TopologicalOrder.Count);

            foreach (var guid in validation.TopologicalOrder)
            {
                var node = nodeMap[guid];
                var entry = new NodeEntry { Node = node };

                if (node is RenderPassNodeData passNode)
                {
                    entry.InputBindings = CompileInputBindings(passNode, portSourceMap);
                    entry.PassThroughBindings = CompilePassThroughBindings(passNode);
                }

                entries.Add(entry);
            }

            compiled.Entries = entries.ToArray();
            return compiled;
        }

        private static InputBinding[] CompileInputBindings(
            RenderPassNodeData passNode,
            Dictionary<string, string> portSourceMap)
        {
            var bindings = new List<InputBinding>();
            foreach (var port in passNode.Ports)
            {
                if (!port.IsInput) continue;
                if (portSourceMap.TryGetValue(port.Id, out var sourcePortId))
                {
                    bindings.Add(new InputBinding
                    {
                        InputPortId = port.Id,
                        SourceOutputPortId = sourcePortId
                    });
                }
            }
            return bindings.ToArray();
        }

        private static PassThroughBinding[] CompilePassThroughBindings(RenderPassNodeData passNode)
        {
            var bindings = new List<PassThroughBinding>();
            foreach (var outPort in passNode.Ports)
            {
                if (outPort.IsInput) continue;

                var matchName = outPort.DisplayName
                    .Replace("Output", "Input")
                    .Replace("Out", "In");

                foreach (var inPort in passNode.Ports)
                {
                    if (!inPort.IsInput || inPort.Type != outPort.Type ||
                        inPort.DisplayName != matchName)
                        continue;

                    bindings.Add(new PassThroughBinding
                    {
                        OutputPortId = outPort.Id,
                        MatchedInputPortId = inPort.Id
                    });
                    break;
                }
            }
            return bindings.ToArray();
        }
    }
}
