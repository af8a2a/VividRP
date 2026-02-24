using System;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class RenderGraphPortData
    {
        public string Id;
        public string DisplayName;
        public PortType Type;
        public bool IsInput;
    }
}
