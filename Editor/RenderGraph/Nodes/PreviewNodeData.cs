using System;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class PreviewNodeData : RenderGraphNodeData
    {
        internal const string TextureInputPortName = "Texture";

        private const string LegacyStateOptionName = "Preview";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<TexturePreviewValue>(LegacyStateOptionName)
                .WithDisplayName("Removed Preview Node");
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<RenderGraphTexture>(TextureInputPortName)
                .WithDisplayName("Legacy Texture")
                .Build();
        }

        internal TexturePreviewValue GetPreviewValue()
        {
            var option = GetNodeOptionByName(LegacyStateOptionName);
            TexturePreviewValue previewValue = new();
            option?.TryGetValue(out previewValue);
            return previewValue ?? new TexturePreviewValue();
        }
    }
}
