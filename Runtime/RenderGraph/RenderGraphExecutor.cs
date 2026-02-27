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
        private CompiledRenderGraph m_Compiled;
        private int m_LastVersion = -1;

        public void Execute(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            RenderGraphAsset asset,
            Camera camera,
            CullingResults cullingResults,
            HistoryResourceManager historyManager)
        {
            EnsureCompiled(asset);

            if (m_Compiled.Warnings != null)
            {
                foreach (var warning in m_Compiled.Warnings)
                    Debug.LogWarning($"[VividRP] {warning}");
            }

            if (!m_Compiled.IsValid)
            {
                if (m_Compiled.Errors != null)
                {
                    foreach (var error in m_Compiled.Errors)
                        Debug.LogError($"[VividRP] {error}");
                }

                Debug.LogError("[VividRP] RenderGraph validation failed, skipping execution.");
                return;
            }

            var slots = new Dictionary<string, ResourceSlot>();

            var creationContext = new ResourceCreationContext
            {
                RenderGraph = renderGraph,
                Camera = camera,
                CullingResults = cullingResults,
                HistoryManager = historyManager
            };

            for (int i = 0; i < m_Compiled.Entries.Length; i++)
            {
                ref var entry = ref m_Compiled.Entries[i];

                if (entry.Node is ResourceNodeData resourceNode)
                    ExecuteResourceNode(resourceNode, creationContext, slots);
                else if (entry.Node is RenderPassNodeData passNode)
                    ExecutePassNode(passNode, ref entry, renderGraph, camera, cullingResults, slots);
            }
        }

        private void EnsureCompiled(RenderGraphAsset asset)
        {
            if (m_Compiled != null && asset.Version == m_LastVersion)
                return;

            m_Compiled = CompiledRenderGraph.Compile(asset);
            m_LastVersion = asset.Version;
        }

        private static void ExecuteResourceNode(
            ResourceNodeData resourceNode,
            ResourceCreationContext creationContext,
            Dictionary<string, ResourceSlot> slots)
        {
            var slot = resourceNode.CreateResource(creationContext);
            foreach (var port in resourceNode.Ports)
            {
                if (!port.IsInput)
                    slots[port.Id] = slot;
            }

            if (resourceNode is IHistoryResourceNode historyNode)
            {
                var historySlot = historyNode.CreateHistorySlot(creationContext);
                if (historySlot.IsValid)
                    slots[historyNode.HistoryPortId] = historySlot;
            }
        }

        private static void ExecutePassNode(
            RenderPassNodeData passNode,
            ref CompiledRenderGraph.NodeEntry entry,
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            Camera camera,
            CullingResults cullingResults,
            Dictionary<string, ResourceSlot> slots)
        {
            // Resolve inputs from pre-compiled bindings
            var resolved = new Dictionary<string, ResourceSlot>();
            if (entry.InputBindings != null)
            {
                foreach (var binding in entry.InputBindings)
                {
                    if (slots.TryGetValue(binding.SourceOutputPortId, out var slot))
                        resolved[binding.InputPortId] = slot;
                }
            }

            var context = new PassExecutionContext(camera, cullingResults, resolved);
            passNode.Record(renderGraph, context);

            // Propagate outputs: prefer explicitly stored, fall back to pre-compiled pass-through
            foreach (var outPort in passNode.Ports)
            {
                if (outPort.IsInput) continue;

                if (context.TryGetOutput(outPort.Id, out var stored) && stored.IsValid)
                {
                    slots[outPort.Id] = stored;
                }
                else if (entry.PassThroughBindings != null)
                {
                    foreach (var pt in entry.PassThroughBindings)
                    {
                        if (pt.OutputPortId != outPort.Id) continue;
                        if (resolved.TryGetValue(pt.MatchedInputPortId, out var s) && s.IsValid)
                            slots[outPort.Id] = s;
                        break;
                    }
                }
            }
        }
    }
}
