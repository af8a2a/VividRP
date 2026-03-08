using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.RenderGraph
{
    [Serializable]
    internal sealed class ClassificationResourceNodeData : RenderGraphNodeData
    {
        internal const string StandardMaterialIndicesOutputPortName = "StandardMaterialIndices";
        internal const string FabricMaterialIndicesOutputPortName = "FabricMaterialIndices";
        internal const string ClearCoatMaterialIndicesOutputPortName = "ClearCoatMaterialIndices";
        internal const string MaterialClassCountsOutputPortName = "MaterialClassCounts";
        internal const string StandardIndirectArgsOutputPortName = "StandardIndirectArgs";
        internal const string FabricIndirectArgsOutputPortName = "FabricIndirectArgs";
        internal const string ClearCoatIndirectArgsOutputPortName = "ClearCoatIndirectArgs";
        
        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<RenderGraphBuffer>(StandardMaterialIndicesOutputPortName)
                .WithDisplayName(StandardMaterialIndicesOutputPortName)
                .Build();

            context.AddOutputPort<RenderGraphBuffer>(FabricMaterialIndicesOutputPortName)
                .WithDisplayName(FabricMaterialIndicesOutputPortName)
                .Build();

            context.AddOutputPort<RenderGraphBuffer>(ClearCoatMaterialIndicesOutputPortName)
                .WithDisplayName(ClearCoatMaterialIndicesOutputPortName)
                .Build();

            context.AddOutputPort<RenderGraphBuffer>(MaterialClassCountsOutputPortName)
                .WithDisplayName(MaterialClassCountsOutputPortName)
                .Build();

            context.AddOutputPort<RenderGraphBuffer>(StandardIndirectArgsOutputPortName)
                .WithDisplayName(StandardIndirectArgsOutputPortName)
                .Build();

            context.AddOutputPort<RenderGraphBuffer>(FabricIndirectArgsOutputPortName)
                .WithDisplayName(FabricIndirectArgsOutputPortName)
                .Build();

            context.AddOutputPort<RenderGraphBuffer>(ClearCoatIndirectArgsOutputPortName)
                .WithDisplayName(ClearCoatIndirectArgsOutputPortName)
                .Build();
        }

        internal IEnumerable<(string PortName, RenderGraphBufferDesc Descriptor)> EnumerateBufferDescriptors()
        {
            yield return (StandardMaterialIndicesOutputPortName, CreateMaterialIndicesDescriptor(StandardMaterialIndicesOutputPortName));
            yield return (FabricMaterialIndicesOutputPortName, CreateMaterialIndicesDescriptor(FabricMaterialIndicesOutputPortName));
            yield return (ClearCoatMaterialIndicesOutputPortName, CreateMaterialIndicesDescriptor(ClearCoatMaterialIndicesOutputPortName));
            yield return (MaterialClassCountsOutputPortName, CreateMaterialCountsDescriptor());
            yield return (StandardIndirectArgsOutputPortName, CreateIndirectArgsDescriptor(StandardIndirectArgsOutputPortName));
            yield return (FabricIndirectArgsOutputPortName, CreateIndirectArgsDescriptor(FabricIndirectArgsOutputPortName));
            yield return (ClearCoatIndirectArgsOutputPortName, CreateIndirectArgsDescriptor(ClearCoatIndirectArgsOutputPortName));
        }

        private static RenderGraphBufferDesc CreateMaterialIndicesDescriptor(string name)
        {
            return new RenderGraphBufferDesc
            {
                Count = 1,
                Stride = sizeof(uint),
                Target = UnityEngine.GraphicsBuffer.Target.Structured,
                Name = name
            };
        }

        private static RenderGraphBufferDesc CreateMaterialCountsDescriptor()
        {
            return new RenderGraphBufferDesc
            {
                Count = 3,
                Stride = sizeof(uint),
                Target = UnityEngine.GraphicsBuffer.Target.Structured,
                Name = MaterialClassCountsOutputPortName
            };
        }

        private static RenderGraphBufferDesc CreateIndirectArgsDescriptor(string name)
        {
            return new RenderGraphBufferDesc
            {
                Count = 4,
                Stride = sizeof(uint),
                Target = UnityEngine.GraphicsBuffer.Target.Structured | UnityEngine.GraphicsBuffer.Target.IndirectArguments,
                Name = name
            };
        }
    }
}
