using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(FullScreenPassNodeData))]
    public class FullScreenPassNodeView : RenderGraphNodeView
    {
        public FullScreenPassNodeView(FullScreenPassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.5f, 0.2f, 0.6f));

            var label = new Label("Draws full-screen UV gradient");
            label.style.color = new StyleColor(new UnityEngine.Color(0.8f, 0.7f, 1f));
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;

            extensionContainer.Add(label);
            RefreshExpandedState();
        }
    }
}
