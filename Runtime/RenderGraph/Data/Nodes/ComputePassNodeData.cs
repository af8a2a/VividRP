using System;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class ComputePassNodeData : RenderGraphNodeData
    {
        public bool AsyncCapable;

        public ComputePassNodeData()
        {
            NodeName = "Compute Pass";
            AddPort("Input Texture", PortType.Texture, true);
            AddPort("Input Buffer", PortType.Buffer, true);
            AddPort("Output Texture", PortType.Texture, false);
            AddPort("Output Buffer", PortType.Buffer, false);
        }
    }
}
