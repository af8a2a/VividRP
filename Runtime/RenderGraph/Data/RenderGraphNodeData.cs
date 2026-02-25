using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

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
            return AddPort(displayName, type, isInput,
                isInput ? AccessFlags.Read : AccessFlags.ReadWrite);
        }

        protected RenderGraphPortData AddPort(string displayName, PortType type, bool isInput, AccessFlags access)
        {
            var port = new RenderGraphPortData
            {
                Id = System.Guid.NewGuid().ToString(),
                DisplayName = displayName,
                Type = type,
                IsInput = isInput,
                Access = access
            };
            Ports.Add(port);
            return port;
        }
    }
}
