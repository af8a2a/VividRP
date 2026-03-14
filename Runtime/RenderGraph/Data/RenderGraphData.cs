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
        RenderList
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

    internal static class RenderGraphPassBindingUtility
    {
        internal static bool UsesInputConnection(RenderGraphPassBindingConnectionKind connectionKind)
        {
            return (connectionKind & RenderGraphPassBindingConnectionKind.Input) != 0;
        }

        internal static bool ConsumesExistingState(RenderGraphPassResourceBinding binding, AccessFlags declaredAccess)
        {
            if ((declaredAccess & AccessFlags.Read) != 0)
                return true;

            return binding != null && UsesInputConnection(binding.ConnectionKind);
        }

        internal static AccessFlags ResolveEffectiveAccess(RenderGraphPassResourceBinding binding, AccessFlags declaredAccess)
        {
            if ((declaredAccess & AccessFlags.Read) != 0)
                return declaredAccess;

            return binding != null && UsesInputConnection(binding.ConnectionKind)
                ? declaredAccess | AccessFlags.Read
                : declaredAccess;
        }
    }

    [Serializable]
    public sealed class RenderGraphPassDefinition
    {
        public string PassType;
        public bool EnableAsyncCompute;
        public List<RenderGraphPassResourceBinding> ResourceBindings = new();
        public List<RenderGraphPassFloatParameter> FloatParameters = new();
        public List<string> PreviewTextureFields = new();
    }

    public sealed class RenderGraphData : ScriptableObject
    {
        public long ImportVersion;

        public List<RenderGraphTextureDesc> TextureDescriptors = new();
        public List<RenderGraphTextureDesc> HistoryTextureDescriptors = new();
        public List<RenderGraphBufferDesc> BufferDescriptors = new();
        public List<RenderGraphRenderListDesc> RenderListDescriptors = new();

        public List<RenderGraphPassDefinition> Passes = new();
    }
}
