using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime.RenderGraph.Resource
{
    public struct ResourceCreationContext
    {
        public UnityEngine.Rendering.RenderGraphModule.RenderGraph RenderGraph;
        public Camera Camera;
        public CullingResults CullingResults;
        public HistoryResourceManager HistoryManager;
    }
}
