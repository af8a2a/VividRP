using UnityEditor.UIElements;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    public class BufferNodeView : RenderGraphNodeView
    {
        public BufferNodeView(BufferNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.2f, 0.7f, 0.7f));

            var count = new IntegerField("Count") { value = data.Count };
            count.RegisterValueChangedCallback(evt => data.Count = evt.newValue);

            var stride = new IntegerField("Stride") { value = data.Stride };
            stride.RegisterValueChangedCallback(evt => data.Stride = evt.newValue);

            var imported = new Toggle("Is Imported") { value = data.IsImported };
            imported.RegisterValueChangedCallback(evt => data.IsImported = evt.newValue);

            extensionContainer.Add(count);
            extensionContainer.Add(stride);
            extensionContainer.Add(imported);
            RefreshExpandedState();
        }
    }
}
