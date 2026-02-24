using System;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class UnsafePassNodeData : RenderGraphNodeData
    {
        public UnsafePassNodeData()
        {
            NodeName = "Unsafe Pass";
            AddPort("Input Texture", PortType.Texture, true);
            AddPort("Input Buffer", PortType.Buffer, true);
            AddPort("Output Texture", PortType.Texture, false);
            AddPort("Output Buffer", PortType.Buffer, false);
        }
    }
}
