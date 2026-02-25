using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [ResourceNode("Buffer")]
    public class BufferNodeData : ResourceNodeData
    {
        public int Count = 1;
        public int Stride = 4;
        public bool IsImported;

        public BufferNodeData()
        {
            NodeName = "Buffer";
            AddPort("Buffer Out", PortType.Buffer, false);
        }

        public override ResourceSlot CreateResource(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            Camera camera)
        {
            var desc = new BufferDesc(Count, Stride)
            {
                name = NodeName
            };
            return ResourceSlot.FromBuffer(renderGraph.CreateBuffer(desc));
        }
    }
}
