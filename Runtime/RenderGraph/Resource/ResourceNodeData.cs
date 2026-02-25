using System;
using UnityEngine;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph.Resource
{
    [Serializable]
    public abstract class ResourceNodeData : RenderGraphNodeData
    {
        public abstract ResourceSlot CreateResource(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            Camera camera);
    }
}
