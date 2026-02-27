using System;
using VividRP.Editor.RenderGraph.Nodes;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph
{
    public static class NodeViewFactory
    {
        public static RenderGraphNodeView Create(RenderGraphNodeData data)
        {
            if (data is RasterPassNodeData rasterPass)
                rasterPass.EnsureBakedDescriptor();

            if (RenderNodeRegistry.TryGetViewType(data.GetType(), out var viewType))
            {
                try
                {
                    return (RenderGraphNodeView)Activator.CreateInstance(viewType, data);
                }
                catch (Exception)
                {
                    // Fall through to default
                }
            }

            return new RenderGraphNodeView(data);
        }
    }
}
