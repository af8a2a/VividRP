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
        private readonly Dictionary<string, ResourceSlot> m_Outputs = new();

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

        public ResourceSlot ResolveInput(string portId)
        {
            if (string.IsNullOrEmpty(portId))
                return default;

            if (m_ResolvedResources.TryGetValue(portId, out var slot))
                return slot;
            return default;
        }

        /// <summary>
        /// Allows a pass to publish a resource it created for an output port,
        /// overriding the default pass-through propagation.
        /// </summary>
        public void StoreOutput(string portId, ResourceSlot slot)
        {
            m_Outputs[portId] = slot;
        }

        internal bool TryGetOutput(string portId, out ResourceSlot slot)
        {
            return m_Outputs.TryGetValue(portId, out slot);
        }
    }
}
