using UnityEngine;

namespace VividRP.Runtime.RenderGraph.Resource
{
    public struct ResourceCreationContext
    {
        public UnityEngine.Rendering.RenderGraphModule.RenderGraph RenderGraph;
        public Camera Camera;
        public HistoryResourceManager HistoryManager;
    }
}
