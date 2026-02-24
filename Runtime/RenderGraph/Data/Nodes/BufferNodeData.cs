using System;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class BufferNodeData : RenderGraphNodeData
    {
        public int Count = 1;
        public int Stride = 4;
        public bool IsImported;

        public BufferNodeData()
        {
            NodeName = "Buffer";
            AddPort("Buffer Out", PortType.Buffer, false);
        }
    }
}
