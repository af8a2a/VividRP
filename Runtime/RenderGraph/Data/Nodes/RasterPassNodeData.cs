using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public class RasterPassNodeData : RenderGraphNodeData
    {
        public int ColorAttachmentCount = 1;
        public bool HasDepth = true;
        public AccessFlags DefaultAccess = AccessFlags.ReadWrite;

        public RasterPassNodeData()
        {
            NodeName = "Raster Pass";
            AddPort("Color In", PortType.Texture, true);
            AddPort("Depth In", PortType.Texture, true);
            AddPort("Color Out", PortType.Texture, false);
            AddPort("Depth Out", PortType.Texture, false);
        }
    }
}
