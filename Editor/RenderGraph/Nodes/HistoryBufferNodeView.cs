using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(HistoryBufferNodeData))]
    public class HistoryBufferNodeView : RenderGraphNodeView
    {
        public HistoryBufferNodeView(HistoryBufferNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.2f, 0.5f, 0.5f));

            var count = new IntegerField("Count") { value = data.Count };
            count.RegisterValueChangedCallback(evt => data.Count = evt.newValue);

            var stride = new IntegerField("Stride") { value = data.Stride };
            stride.RegisterValueChangedCallback(evt => data.Stride = evt.newValue);

            var label = new Label("Double-buffered (Current + History)");
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;
            label.style.color = new StyleColor(new UnityEngine.Color(0.7f, 0.7f, 0.7f));

            extensionContainer.Add(count);
            extensionContainer.Add(stride);
            extensionContainer.Add(label);
            RefreshExpandedState();
        }
    }
}
