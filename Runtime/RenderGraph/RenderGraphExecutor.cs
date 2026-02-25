using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Data;
using VividRP.Runtime.RenderGraph.Passes;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph
{
    public class RenderGraphExecutor
    {
        public void Execute(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            RenderGraphAsset asset,
            Camera camera,
            CullingResults cullingResults)
        {
            var validation = asset.Validate();
            if (!validation.IsValid)
            {
                Debug.LogError("[VividRP] RenderGraph validation failed, skipping execution.");
                return;
            }

            var nodeMap = new Dictionary<string, RenderGraphNodeData>();
            foreach (var node in asset.Nodes)
                nodeMap[node.Guid] = node;

            var portSourceMap = new Dictionary<string, (string nodeGuid, string portId)>();
            foreach (var edge in asset.Edges)
                portSourceMap[edge.InputPortId] = (edge.OutputNodeGuid, edge.OutputPortId);

            // Unified resource slot map keyed by port ID
            var slots = new Dictionary<string, ResourceSlot>();

            foreach (var guid in validation.TopologicalOrder)
            {
                var node = nodeMap[guid];

                if (node is ResourceNodeData resourceNode)
                {
                    var slot = resourceNode.CreateResource(renderGraph, camera);
                    foreach (var port in resourceNode.Ports)
                    {
                        if (!port.IsInput)
                            slots[port.Id] = slot;
                    }
                }
                else if (node is RenderPassNodeData passNode)
                {
                    // Resolve input slots for this pass
                    var resolved = new Dictionary<string, ResourceSlot>();
                    foreach (var port in passNode.Ports)
                    {
                        if (!port.IsInput) continue;
                        if (portSourceMap.TryGetValue(port.Id, out var source) &&
                            slots.TryGetValue(source.portId, out var slot))
                        {
                            resolved[port.Id] = slot;
                        }
                    }

                    var context = new PassExecutionContext(camera, cullingResults, resolved);
                    passNode.Record(renderGraph, context);

                    // Propagate outputs: prefer explicitly stored outputs, fall back to pass-through
                    foreach (var outPort in passNode.Ports)
                    {
                        if (outPort.IsInput) continue;

                        if (context.TryGetOutput(outPort.Id, out var stored) && stored.IsValid)
                            slots[outPort.Id] = stored;
                        else
                            PropagateOutput(outPort, passNode, resolved, slots);
                    }
                }
            }
        }

        private static void PropagateOutput(
            RenderGraphPortData outputPort,
            RenderGraphNodeData node,
            Dictionary<string, ResourceSlot> resolvedInputs,
            Dictionary<string, ResourceSlot> slots)
        {
            var matchName = outputPort.DisplayName.Replace("Output", "Input")
                                                  .Replace("Out", "In");
            foreach (var inPort in node.Ports)
            {
                if (!inPort.IsInput || inPort.Type != outputPort.Type ||
                    inPort.DisplayName != matchName)
                    continue;

                if (resolvedInputs.TryGetValue(inPort.Id, out var slot) && slot.IsValid)
                    slots[outputPort.Id] = slot;
                break;
            }
        }
    }
}
