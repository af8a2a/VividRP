using System;
using Unity.GraphToolkit.Editor;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class AccelerationStructureResourceNodeData : RenderGraphNodeData
    {
        internal const string InputPortName = "AccelerationStructureInput";
        internal const string OutputPortName = "AccelerationStructure";

        private const string DescriptorOptionName = "Descriptor";

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<RenderGraphAccelerationStructureDesc>(DescriptorOptionName)
                .WithDisplayName("Acceleration Structure Descriptor")
                .WithDefaultValue(new RenderGraphAccelerationStructureDesc());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<RenderGraphAccelerationStructure>(OutputPortName)
                .WithDisplayName("Out")
                .Build();
        }

        internal RenderGraphAccelerationStructureDesc GetDescriptor()
        {
            var option = GetNodeOptionByName(DescriptorOptionName);
            option.TryGetValue<RenderGraphAccelerationStructureDesc>(out var desc);
            return desc ?? new RenderGraphAccelerationStructureDesc();
        }
    }
}
