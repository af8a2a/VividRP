using System;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class HistoryResourceNodeData : RenderGraphNodeData
    {
        internal const string PreviousOutputPortName = "PrevOut";
        internal const string CurrentOutputPortName = "CurrOut";

        private const string DescriptorOptionName = "Descriptor";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<RenderGraphTextureDesc>(DescriptorOptionName)
                .WithDisplayName("History Descriptor")
                .WithDefaultValue(new RenderGraphTextureDesc());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<RenderGraphTexture>(PreviousOutputPortName)
                .WithDisplayName("PrevOut")
                .Build();

            context.AddOutputPort<RenderGraphTexture>(CurrentOutputPortName)
                .WithDisplayName("CurrOut")
                .Build();
        }

        internal RenderGraphTextureDesc GetDescriptor()
        {
            var option = GetNodeOptionByName(DescriptorOptionName);
            option.TryGetValue<RenderGraphTextureDesc>(out var desc);
            return desc ?? new RenderGraphTextureDesc();
        }

        internal bool IsPreviousOutputPort(IPort port)
        {
            return ReferenceEquals(GetOutputPortByName(PreviousOutputPortName), port);
        }

        internal bool IsCurrentOutputPort(IPort port)
        {
            return ReferenceEquals(GetOutputPortByName(CurrentOutputPortName), port);
        }
    }
}
