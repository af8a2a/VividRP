using System;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class BufferResourceNodeData : RenderGraphNodeData
    {
        internal const string InputPortName = "BufferInput";
        internal const string OutputPortName = "Buffer";

        private const string DescriptorOptionName = "Descriptor";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<RenderGraphBufferDesc>(DescriptorOptionName)
                .WithDisplayName("Buffer Descriptor")
                .WithDefaultValue(new RenderGraphBufferDesc());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<RenderGraphBuffer>(OutputPortName)
                .WithDisplayName("Out")
                .Build();
        }

        internal RenderGraphBufferDesc GetDescriptor()
        {
            var option = GetNodeOptionByName(DescriptorOptionName);
            option.TryGetValue<RenderGraphBufferDesc>(out var desc);
            return desc ?? new RenderGraphBufferDesc();
        }
    }
}
