using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Resource;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    [ResourceNode("Texture")]
    public class TextureNodeData : ResourceNodeData
    {
        public int Width = 1920;
        public int Height = 1080;
        public GraphicsFormat Format = GraphicsFormat.R8G8B8A8_SRGB;
        public bool IsImported;

        public TextureNodeData()
        {
            NodeName = "Texture";
            AddPort("Texture Out", PortType.Texture, false);
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
            return ResourceSlot.FromTexture(context.RenderGraph.CreateTexture(desc));
        }
    }
}
