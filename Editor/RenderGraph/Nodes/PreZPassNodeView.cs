using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(PreZPassNodeData))]
    public class PreZPassNodeView : RenderGraphNodeView
    {
        public PreZPassNodeView(PreZPassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new Color(0.1f, 0.6f, 0.6f));

            var layerMask = new LayerMaskField("Layer Mask") { value = data.RenderingLayerMask };
            layerMask.RegisterValueChangedCallback(evt => data.RenderingLayerMask = evt.newValue);

            extensionContainer.Add(layerMask);
            RefreshExpandedState();
        }
    }
}
