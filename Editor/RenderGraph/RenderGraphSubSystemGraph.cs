using System;
using Unity.GraphToolkit.Editor;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    [Subgraph(typeof(RenderGraphEditorGraph))]
    [Graph(AssetExtension)]
    internal sealed class RenderGraphSubSystemGraph : RenderGraphEditorGraph
    {
        internal const string AssetExtension = "vrdgsub";
    }
}
