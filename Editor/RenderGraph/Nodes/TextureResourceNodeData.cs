using System;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class TextureResourceNodeData : RenderGraphNodeData
    {
        internal const string OutputPortName = "Texture";

        private const string DescriptorOptionName = "Descriptor";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<RenderGraphTextureDesc>(DescriptorOptionName)
                .WithDisplayName("Texture Descriptor")
                .WithDefaultValue(new RenderGraphTextureDesc());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<RenderGraphTexture>(OutputPortName)
                .WithDisplayName("Out")
                .Build();
        }

        internal RenderGraphTextureDesc GetDescriptor()
        {
            var option = GetNodeOptionByName(DescriptorOptionName);
            option.TryGetValue<RenderGraphTextureDesc>(out var desc);
            return desc ?? new RenderGraphTextureDesc();
        }
    }
}
