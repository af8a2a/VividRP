using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(UnsafePassNodeData))]
    public class UnsafePassNodeView : RenderGraphNodeView
    {
        public UnsafePassNodeView(UnsafePassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.9f, 0.5f, 0.1f));

            var warning = new Label("Full command buffer access");
            warning.style.color = new StyleColor(new UnityEngine.Color(1f, 0.8f, 0.2f));
            warning.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;

            extensionContainer.Add(warning);
            RefreshExpandedState();
        }
    }
}
