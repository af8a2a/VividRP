using System;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class RenderGraphEdgeData
    {
        public string OutputNodeGuid;
        public string OutputPortId;
        public string InputNodeGuid;
        public string InputPortId;
    }
}
