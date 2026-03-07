using System;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class RenderListResourceNodeData : RenderGraphNodeData
    {
        internal const string InputPortName = "RenderListInput";
        internal const string OutputPortName = "RenderList";

        private const string DescriptorOptionName = "Descriptor";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<RenderGraphRenderListDesc>(DescriptorOptionName)
                .WithDisplayName("Render List Descriptor")
                .WithDefaultValue(new RenderGraphRenderListDesc());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<RenderGraphRenderList>(OutputPortName)
                .WithDisplayName("Out")
                .Build();
        }

        internal RenderGraphRenderListDesc GetDescriptor()
        {
            var option = GetNodeOptionByName(DescriptorOptionName);
            option.TryGetValue<RenderGraphRenderListDesc>(out var desc);
            return desc ?? new RenderGraphRenderListDesc();
        }
    }
}
