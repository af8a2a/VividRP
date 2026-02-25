using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(FinalBlitPassNodeData))]
    public class FinalBlitPassNodeView : RenderGraphNodeView
    {
        public FinalBlitPassNodeView(FinalBlitPassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.8f, 0.2f, 0.2f));

            var label = new Label("Blits to camera back buffer");
            label.style.color = new StyleColor(new UnityEngine.Color(1f, 0.8f, 0.8f));
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;

            extensionContainer.Add(label);
            RefreshExpandedState();
        }
    }
}
