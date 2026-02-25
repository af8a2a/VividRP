using System;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph.Passes
{
    [Serializable]
    public abstract class RenderPassNodeData : RenderGraphNodeData
    {
        public abstract PassType Type { get; }

        public abstract void Record(
            UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph,
            PassExecutionContext context);
    }
}
