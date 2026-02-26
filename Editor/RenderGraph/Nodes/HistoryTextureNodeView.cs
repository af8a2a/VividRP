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

            var sizeMode = new EnumField("Size Mode", data.SizeMode);
            extensionContainer.Add(sizeMode);

            var width = new IntegerField("Width") { value = data.Width };
            width.RegisterValueChangedCallback(evt => data.Width = evt.newValue);

            var height = new IntegerField("Height") { value = data.Height };
            height.RegisterValueChangedCallback(evt => data.Height = evt.newValue);

            var scale = new FloatField("Scale") { value = data.Scale };
            scale.RegisterValueChangedCallback(evt => data.Scale = evt.newValue);

            extensionContainer.Add(width);
            extensionContainer.Add(height);
            extensionContainer.Add(scale);

            void UpdateSizeModeVisibility(TextureSizeMode mode)
            {
                bool isExplicit = mode == TextureSizeMode.Explicit;
                width.style.display = isExplicit ? DisplayStyle.Flex : DisplayStyle.None;
                height.style.display = isExplicit ? DisplayStyle.Flex : DisplayStyle.None;
                scale.style.display = isExplicit ? DisplayStyle.None : DisplayStyle.Flex;
            }

            sizeMode.RegisterValueChangedCallback(evt =>
            {
                data.SizeMode = (TextureSizeMode)evt.newValue;
                UpdateSizeModeVisibility(data.SizeMode);
            });
            UpdateSizeModeVisibility(data.SizeMode);

            var format = new EnumField("Format", data.Format);
            format.RegisterValueChangedCallback(evt => data.Format = (GraphicsFormat)evt.newValue);
            extensionContainer.Add(format);

            var uav = new Toggle("Enable UAV") { value = data.EnableRandomWrite };
            uav.RegisterValueChangedCallback(evt => data.EnableRandomWrite = evt.newValue);
            extensionContainer.Add(uav);

            var label = new Label("Double-buffered (Current + History)");
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;
            label.style.color = new StyleColor(new UnityEngine.Color(0.7f, 0.7f, 0.7f));
            extensionContainer.Add(label);

            RefreshExpandedState();
        }
    }
}
