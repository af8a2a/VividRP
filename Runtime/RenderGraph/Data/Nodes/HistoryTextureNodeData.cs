using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [ResourceNode("History Texture")]
    public class HistoryTextureNodeData : ResourceNodeData, IHistoryResourceNode
    {
        public int Width = 1920;
        public int Height = 1080;
        public GraphicsFormat Format = GraphicsFormat.R8G8B8A8_SRGB;

        public string HistoryPortId
        {
            get
            {
                foreach (var port in Ports)
                {
                    if (!port.IsInput && port.DisplayName == "History")
                        return port.Id;
                }
                return null;
            }
        }

        public HistoryTextureNodeData()
        {
            NodeName = "History Texture";
            AddPort("Current", PortType.Texture, false, AccessFlags.ReadWrite);
            AddPort("History", PortType.Texture, false, AccessFlags.Read);
        }

        public override ResourceSlot CreateResource(ResourceCreationContext context)
        {
            var desc = new TextureDesc(Width, Height)
            {
                colorFormat = Format,
                clearBuffer = true,
                clearColor = Color.clear,
                name = NodeName
            };

            var rtHandle = context.HistoryManager.GetOrAllocate(Guid, desc);
            return ResourceSlot.FromTexture(context.RenderGraph.ImportTexture(rtHandle));
        }

        public ResourceSlot CreateHistorySlot(ResourceCreationContext context)
        {
            var rtHandle = context.HistoryManager.GetHistoryHandle(Guid);
            if (rtHandle == null)
                return default;

            return ResourceSlot.FromTexture(context.RenderGraph.ImportTexture(rtHandle));
        }
    }
}
