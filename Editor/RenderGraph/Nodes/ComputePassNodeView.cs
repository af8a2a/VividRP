using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(ComputePassNodeData))]
    public class ComputePassNodeView : RenderGraphNodeView
    {
        public ComputePassNodeView(ComputePassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.2f, 0.7f, 0.3f));

            var async = new Toggle("Async Capable") { value = data.AsyncCapable };
            async.RegisterValueChangedCallback(evt => data.AsyncCapable = evt.newValue);

            extensionContainer.Add(async);
            RefreshExpandedState();
        }
    }
}
