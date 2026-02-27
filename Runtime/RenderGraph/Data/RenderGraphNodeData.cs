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
            return AddPort(displayName, type, isInput, access, InferIntent(isInput, access));
        }

        protected RenderGraphPortData AddPort(
            string displayName,
            PortType type,
            bool isInput,
            AccessFlags access,
            ResourceIntent intent)
        {
            var port = new RenderGraphPortData
            {
                Id = System.Guid.NewGuid().ToString(),
                DisplayName = displayName,
                Type = type,
                IsInput = isInput,
                Access = access,
                Intent = intent
            };
            Ports.Add(port);
            return port;
        }

        private static ResourceIntent InferIntent(bool isInput, AccessFlags access)
        {
            return access switch
            {
                AccessFlags.Write => ResourceIntent.Write,
                AccessFlags.WriteAll => ResourceIntent.Write,
                AccessFlags.ReadWrite => ResourceIntent.ReadWrite,
                _ => isInput ? ResourceIntent.Read : ResourceIntent.ReadWrite
            };
        }
    }
}
