using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph
{
    public class RenderGraphExecutor
    {
        private class PassData
        {
        }

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

            // Build lookup: guid → node
            var nodeMap = new Dictionary<string, RenderGraphNodeData>();
            foreach (var node in asset.Nodes)
                nodeMap[node.Guid] = node;

            // Build edge lookup: inputPortId → (outputNodeGuid, outputPortId)
            var portSourceMap = new Dictionary<string, (string nodeGuid, string portId)>();
            foreach (var edge in asset.Edges)
                portSourceMap[edge.InputPortId] = (edge.OutputNodeGuid, edge.OutputPortId);

            // Create resources for resource nodes in topological order
            var textureHandles = new Dictionary<string, TextureHandle>();
            var bufferHandles = new Dictionary<string, BufferHandle>();

            foreach (var guid in validation.TopologicalOrder)
            {
                var node = nodeMap[guid];

                switch (node)
                {
                    case TextureNodeData texNode:
                    {
                        var handle = CreateTexture(renderGraph, texNode, camera);
                        // Map each output port to this handle
                        foreach (var port in texNode.Ports)
                        {
                            if (!port.IsInput)
                                textureHandles[port.Id] = handle;
                        }
                        break;
                    }
                    case BufferNodeData bufNode:
                    {
                        var handle = CreateBuffer(renderGraph, bufNode);
                        foreach (var port in bufNode.Ports)
                        {
                            if (!port.IsInput)
                                bufferHandles[port.Id] = handle;
                        }
                        break;
                    }
                    case RasterPassNodeData rasterNode:
                        RecordRasterPass(renderGraph, rasterNode, portSourceMap,
                            textureHandles, bufferHandles);
                        break;
                    case ComputePassNodeData computeNode:
                        RecordComputePass(renderGraph, computeNode, portSourceMap,
                            textureHandles, bufferHandles);
                        break;
                    case UnsafePassNodeData unsafeNode:
                        RecordUnsafePass(renderGraph, unsafeNode, portSourceMap,
                            textureHandles, bufferHandles);
                        break;
                }
            }
        }

        private TextureHandle CreateTexture(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            TextureNodeData data, Camera camera)
        {
            var desc = new TextureDesc(data.Width, data.Height)
            {
                colorFormat = data.Format,
                clearBuffer = true,
                clearColor = Color.clear,
                name = data.NodeName
            };
            return renderGraph.CreateTexture(desc);
        }

        private BufferHandle CreateBuffer(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            BufferNodeData data)
        {
            var desc = new BufferDesc(data.Count, data.Stride)
            {
                name = data.NodeName
            };
            return renderGraph.CreateBuffer(desc);
        }

        private TextureHandle ResolveInputTexture(
            RenderGraphPortData inputPort,
            Dictionary<string, (string nodeGuid, string portId)> portSourceMap,
            Dictionary<string, TextureHandle> textureHandles)
        {
            if (portSourceMap.TryGetValue(inputPort.Id, out var source) &&
                textureHandles.TryGetValue(source.portId, out var handle))
                return handle;
            return TextureHandle.nullHandle;
        }

        private BufferHandle ResolveInputBuffer(
            RenderGraphPortData inputPort,
            Dictionary<string, (string nodeGuid, string portId)> portSourceMap,
            Dictionary<string, BufferHandle> bufferHandles)
        {
            if (portSourceMap.TryGetValue(inputPort.Id, out var source) &&
                bufferHandles.TryGetValue(source.portId, out var handle))
                return handle;
            return BufferHandle.nullHandle;
        }

        private void RecordRasterPass(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            RasterPassNodeData node,
            Dictionary<string, (string nodeGuid, string portId)> portSourceMap,
            Dictionary<string, TextureHandle> textureHandles,
            Dictionary<string, BufferHandle> bufferHandles)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(
                node.NodeName, out _);

            int colorIndex = 0;
            foreach (var port in node.Ports)
            {
                if (port.IsInput && port.Type == PortType.Texture)
                {
                    var tex = ResolveInputTexture(port, portSourceMap, textureHandles);
                    if (!tex.IsValid()) continue;

                    if (port.DisplayName.Contains("Depth"))
                        builder.SetRenderAttachmentDepth(tex);
                    else
                        builder.SetRenderAttachment(tex, colorIndex++);
                }
            }

            // Map output ports to the same handles as their connected inputs
            // so downstream passes can reference them
            foreach (var port in node.Ports)
            {
                if (!port.IsInput && port.Type == PortType.Texture)
                {
                    // Find the corresponding input port's texture
                    var matchName = port.DisplayName.Replace("Out", "In");
                    foreach (var inPort in node.Ports)
                    {
                        if (inPort.IsInput && inPort.DisplayName == matchName)
                        {
                            var tex = ResolveInputTexture(inPort, portSourceMap, textureHandles);
                            if (tex.IsValid())
                                textureHandles[port.Id] = tex;
                            break;
                        }
                    }
                }
            }

            builder.SetRenderFunc<PassData>((_, _) => { });
        }

        private void RecordComputePass(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            ComputePassNodeData node,
            Dictionary<string, (string nodeGuid, string portId)> portSourceMap,
            Dictionary<string, TextureHandle> textureHandles,
            Dictionary<string, BufferHandle> bufferHandles)
        {
            using var builder = renderGraph.AddComputePass<PassData>(
                node.NodeName, out _);

            foreach (var port in node.Ports)
            {
                if (!port.IsInput) continue;

                if (port.Type == PortType.Texture)
                {
                    var tex = ResolveInputTexture(port, portSourceMap, textureHandles);
                    if (tex.IsValid())
                        builder.UseTexture(tex);
                }
                else if (port.Type == PortType.Buffer)
                {
                    var buf = ResolveInputBuffer(port, portSourceMap, bufferHandles);
                    if (buf.IsValid())
                        builder.UseBuffer(buf);
                }
            }

            // Propagate outputs
            foreach (var port in node.Ports)
            {
                if (port.IsInput) continue;
                PropagateOutput(port, node, portSourceMap, textureHandles, bufferHandles);
            }

            builder.SetRenderFunc<PassData>((_, _) => { });
        }

        private void RecordUnsafePass(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            UnsafePassNodeData node,
            Dictionary<string, (string nodeGuid, string portId)> portSourceMap,
            Dictionary<string, TextureHandle> textureHandles,
            Dictionary<string, BufferHandle> bufferHandles)
        {
            using var builder = renderGraph.AddUnsafePass<PassData>(
                node.NodeName, out _);

            foreach (var port in node.Ports)
            {
                if (!port.IsInput) continue;

                if (port.Type == PortType.Texture)
                {
                    var tex = ResolveInputTexture(port, portSourceMap, textureHandles);
                    if (tex.IsValid())
                        builder.UseTexture(tex);
                }
                else if (port.Type == PortType.Buffer)
                {
                    var buf = ResolveInputBuffer(port, portSourceMap, bufferHandles);
                    if (buf.IsValid())
                        builder.UseBuffer(buf);
                }
            }

            // Propagate outputs
            foreach (var port in node.Ports)
            {
                if (port.IsInput) continue;
                PropagateOutput(port, node, portSourceMap, textureHandles, bufferHandles);
            }

            builder.SetRenderFunc<PassData>((_, _) => { });
        }

        private void PropagateOutput(
            RenderGraphPortData outputPort,
            RenderGraphNodeData node,
            Dictionary<string, (string nodeGuid, string portId)> portSourceMap,
            Dictionary<string, TextureHandle> textureHandles,
            Dictionary<string, BufferHandle> bufferHandles)
        {
            // Find matching input port by name pattern (Output X → Input X)
            var matchName = outputPort.DisplayName.Replace("Output", "Input");
            foreach (var inPort in node.Ports)
            {
                if (!inPort.IsInput || inPort.Type != outputPort.Type ||
                    inPort.DisplayName != matchName)
                    continue;

                if (outputPort.Type == PortType.Texture)
                {
                    var tex = ResolveInputTexture(inPort, portSourceMap, textureHandles);
                    if (tex.IsValid())
                        textureHandles[outputPort.Id] = tex;
                }
                else if (outputPort.Type == PortType.Buffer)
                {
                    var buf = ResolveInputBuffer(inPort, portSourceMap, bufferHandles);
                    if (buf.IsValid())
                        bufferHandles[outputPort.Id] = buf;
                }
                break;
            }
        }
    }
}
