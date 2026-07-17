using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public enum RenderGraphResourceKind
    {
        Texture,
        Buffer,
        RenderList,
        AccelerationStructure
    }

    public enum RenderGraphPassBindingSourceKind
    {
        Resource,
        PassField
    }

    [Flags]
    public enum RenderGraphPassBindingConnectionKind
    {
        None = 0,
        Input = 1 << 0,
        Output = 1 << 1,
    }

    public enum RenderGraphResourceBindingVariant
    {
        Default,
        HistoryPrevious,
        HistoryCurrent
    }

    [Serializable]
    public sealed class RenderGraphPassResourceBinding
    {
        public string FieldName;
        public RenderGraphResourceKind ResourceKind;
        public int ResourceIndex;
        public RenderGraphResourceBindingVariant ResourceBindingVariant;
        public RenderGraphPassBindingSourceKind SourceKind;
        public RenderGraphPassBindingConnectionKind ConnectionKind;
        public int SourcePassIndex = -1;
        public string SourceFieldName;
    }

    [Serializable]
    public sealed class RenderGraphPassFloatParameter
    {
        public string FieldName;
        public float Value;
    }

    [Serializable]
    public sealed class RenderGraphPassEnumParameter
    {
        public string FieldName;
        public int Value;
    }

    [Serializable]
    public sealed class RenderGraphPassRenderListDescParameter
    {
        public string FieldName;
        public RenderGraphRenderListDesc Value;
    }

    internal static class RenderGraphPassBindingUtility
    {
        internal static bool UsesInputConnection(RenderGraphPassBindingConnectionKind connectionKind)
        {
            return (connectionKind & RenderGraphPassBindingConnectionKind.Input) != 0;
        }

        internal static bool UsesOutputConnection(RenderGraphPassBindingConnectionKind connectionKind)
        {
            return (connectionKind & RenderGraphPassBindingConnectionKind.Output) != 0;
        }

        internal static bool ConsumesExistingState(RenderGraphPassResourceBinding binding, AccessFlags declaredAccess)
        {
            var effectiveAccess = ResolveEffectiveAccess(binding, declaredAccess);
            return (effectiveAccess & AccessFlags.Read) != 0;
        }

        internal static AccessFlags ResolveEffectiveAccess(RenderGraphPassResourceBinding binding, AccessFlags declaredAccess)
        {
            if (binding == null)
                return declaredAccess;

            var canRead = (declaredAccess & AccessFlags.Read) != 0;
            var canWrite = (declaredAccess & AccessFlags.Write) != 0;
            if (canRead && canWrite)
            {
                if (UsesInputConnection(binding.ConnectionKind))
                    return declaredAccess;

                if (UsesOutputConnection(binding.ConnectionKind))
                    return declaredAccess & ~AccessFlags.Read;
            }

            if (canRead)
                return declaredAccess;

            return UsesInputConnection(binding.ConnectionKind)
                ? declaredAccess | AccessFlags.Read
                : declaredAccess;
        }
    }

    [Serializable]
    public sealed class RenderGraphPassDefinition
    {
        public string PassType;
        public string PassName;
        public bool EnableAsyncCompute;
        public List<RenderGraphPassResourceBinding> ResourceBindings = new();
        public List<RenderGraphPassFloatParameter> FloatParameters = new();
        public List<RenderGraphPassEnumParameter> EnumParameters = new();
        public List<RenderGraphPassRenderListDescParameter> RenderListDescParameters = new();
    }

    public sealed class RenderGraphData : ScriptableObject
    {
        public long ImportVersion;

        public List<RenderGraphTextureDesc> TextureDescriptors = new();
        public List<RenderGraphTextureDesc> HistoryTextureDescriptors = new();
        public List<RenderGraphBufferDesc> BufferDescriptors = new();
        public List<RenderGraphRenderListDesc> RenderListDescriptors = new();
        public List<RenderGraphAccelerationStructureDesc> AccelerationStructureDescriptors = new();

        public List<RenderGraphPassDefinition> Passes = new();
    }
}
