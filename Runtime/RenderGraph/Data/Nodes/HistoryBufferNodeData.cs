using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [ResourceNode("History Buffer")]
    public class HistoryBufferNodeData : ResourceNodeData, IHistoryResourceNode
    {
        public int Count = 1;
        public int Stride = 4;

        public string HistoryPortId
        {
            get
            {
                foreach (var port in Ports)
                {
                    if (!port.IsInput && port.DisplayName == "History")
                        return port.Id;
                }
                return null;
            }
        }

        public HistoryBufferNodeData()
        {
            NodeName = "History Buffer";
            AddPort("Current", PortType.Buffer, false, AccessFlags.ReadWrite);
            AddPort("History", PortType.Buffer, false, AccessFlags.Read);
        }

        public override ResourceSlot CreateResource(ResourceCreationContext context)
        {
            var buffer = context.HistoryManager.GetOrAllocateBuffer(Guid, Count, Stride);
            return ResourceSlot.FromBuffer(context.RenderGraph.ImportBuffer(buffer));
        }

        public ResourceSlot CreateHistorySlot(ResourceCreationContext context)
        {
            var buffer = context.HistoryManager.GetHistoryBufferHandle(Guid);
            if (buffer == null)
                return default;

            return ResourceSlot.FromBuffer(context.RenderGraph.ImportBuffer(buffer));
        }
    }
}
