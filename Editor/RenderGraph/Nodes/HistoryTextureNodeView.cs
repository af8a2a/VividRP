using UnityEditor.UIElements;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(HistoryTextureNodeData))]
    public class HistoryTextureNodeView : RenderGraphNodeView
    {
        public HistoryTextureNodeView(HistoryTextureNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.2f, 0.6f, 0.6f));

            var width = new IntegerField("Width") { value = data.Width };
            width.RegisterValueChangedCallback(evt => data.Width = evt.newValue);

            var height = new IntegerField("Height") { value = data.Height };
            height.RegisterValueChangedCallback(evt => data.Height = evt.newValue);

            var format = new EnumField("Format", data.Format);
            format.RegisterValueChangedCallback(evt => data.Format = (GraphicsFormat)evt.newValue);

            var label = new Label("Double-buffered (Current + History)");
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;
            label.style.color = new StyleColor(new UnityEngine.Color(0.7f, 0.7f, 0.7f));

            extensionContainer.Add(width);
            extensionContainer.Add(height);
            extensionContainer.Add(format);
            extensionContainer.Add(label);
            RefreshExpandedState();
        }
    }
}
