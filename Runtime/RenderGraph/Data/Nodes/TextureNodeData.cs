using System;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class TextureNodeData : RenderGraphNodeData
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
    }
}
