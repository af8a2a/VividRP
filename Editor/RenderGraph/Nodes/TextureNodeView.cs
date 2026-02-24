using UnityEditor.UIElements;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    public class TextureNodeView : RenderGraphNodeView
    {
        public TextureNodeView(TextureNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.6f, 0.3f, 0.8f));

            var width = new IntegerField("Width") { value = data.Width };
            width.RegisterValueChangedCallback(evt => data.Width = evt.newValue);

            var height = new IntegerField("Height") { value = data.Height };
            height.RegisterValueChangedCallback(evt => data.Height = evt.newValue);

            var format = new EnumField("Format", data.Format);
            format.RegisterValueChangedCallback(evt => data.Format = (GraphicsFormat)evt.newValue);

            var imported = new Toggle("Is Imported") { value = data.IsImported };
            imported.RegisterValueChangedCallback(evt => data.IsImported = evt.newValue);

            extensionContainer.Add(width);
            extensionContainer.Add(height);
            extensionContainer.Add(format);
            extensionContainer.Add(imported);
            RefreshExpandedState();
        }
    }
}
