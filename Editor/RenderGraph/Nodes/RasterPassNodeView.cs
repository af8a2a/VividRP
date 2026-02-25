
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.UIElements;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Editor.RenderGraph.Nodes
{
    [NodeEditor(typeof(RasterPassNodeData))]
    public class RasterPassNodeView : RenderGraphNodeView
    {
        public RasterPassNodeView(RasterPassNodeData data) : base(data)
        {
            titleContainer.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.2f, 0.4f, 0.8f));

            var attachments = new IntegerField("Color Attachments") { value = data.ColorAttachmentCount };
            attachments.RegisterValueChangedCallback(evt => data.ColorAttachmentCount = evt.newValue);

            var depth = new Toggle("Has Depth") { value = data.HasDepth };
            depth.RegisterValueChangedCallback(evt => data.HasDepth = evt.newValue);

            var access = new EnumField("Default Access", data.DefaultAccess);
            access.RegisterValueChangedCallback(evt => data.DefaultAccess = (AccessFlags)evt.newValue);

            extensionContainer.Add(attachments);
            extensionContainer.Add(depth);
            extensionContainer.Add(access);
            RefreshExpandedState();
        }
    }
}
