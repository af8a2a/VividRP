using System;
using Unity.GraphToolkit.Editor;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    [UseWithGraph(
        typeof(RenderGraphEditorGraph),
        typeof(RenderGraphSubSystemGraph))]
    internal abstract class RenderGraphNodeData : Node
    {
    }
}
