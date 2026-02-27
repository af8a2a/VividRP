using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class RenderGraphPortData
    {
        public string Id;
        public string DisplayName;
        public PortType Type;
        public bool IsInput;
        public AccessFlags Access;
        public ResourceIntent Intent;
    }
}
