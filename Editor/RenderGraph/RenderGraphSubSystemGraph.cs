using System;
using Unity.GraphToolkit.Editor;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    [Subgraph(typeof(RenderGraphEditorGraph))]
    [Graph(
        AssetExtension,
        GraphOptions.DisableAutoInclusionOfNodesFromGraphAssembly)]
    internal sealed class RenderGraphSubSystemGraph : RenderGraphEditorGraph
    {
        internal const string AssetExtension = "vrdgsub";
    }
}
