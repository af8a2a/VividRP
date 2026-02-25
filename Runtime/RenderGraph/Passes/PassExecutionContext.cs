using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime.RenderGraph.Data;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Passes
{
    public class PassExecutionContext
    {
        public Camera Camera { get; }
        public CullingResults CullingResults { get; }

        private readonly Dictionary<string, ResourceSlot> m_ResolvedResources;

        public PassExecutionContext(
            Camera camera,
            CullingResults cullingResults,
            Dictionary<string, ResourceSlot> resolvedResources)
        {
            Camera = camera;
            CullingResults = cullingResults;
            m_ResolvedResources = resolvedResources;
        }

        public ResourceSlot ResolveInput(RenderGraphPortData port)
        {
            if (m_ResolvedResources.TryGetValue(port.Id, out var slot))
                return slot;
            return default;
        }
    }
}
