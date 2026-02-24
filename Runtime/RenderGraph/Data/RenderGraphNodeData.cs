using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class RenderGraphNodeData
    {
        public string Guid;
        public Vector2 Position;
        public string NodeName;
        public List<RenderGraphPortData> Ports = new List<RenderGraphPortData>();

        public RenderGraphNodeData()
        {
            Guid = System.Guid.NewGuid().ToString();
        }

        protected RenderGraphPortData AddPort(string displayName, PortType type, bool isInput)
        {
            var port = new RenderGraphPortData
            {
                Id = System.Guid.NewGuid().ToString(),
                DisplayName = displayName,
                Type = type,
                IsInput = isInput
            };
            Ports.Add(port);
            return port;
        }
    }
}
