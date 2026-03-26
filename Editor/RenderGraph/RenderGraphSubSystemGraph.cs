using System;
using Unity.GraphToolkit.Editor;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    [Subgraph(typeof(RenderGraphEditorGraph))]
    [Graph(RenderGraphEditorGraph.AssetExtension)]
    internal sealed class RenderGraphSubSystemGraph : RenderGraphEditorGraph
    {
    }
}
