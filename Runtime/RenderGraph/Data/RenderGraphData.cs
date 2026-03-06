using System;
using System.Collections.Generic;
using UnityEngine;

namespace VividRP.Runtime
{
    public enum RenderGraphResourceKind
    {
        Texture,
        Buffer
    }

    public enum RenderGraphPassBindingSourceKind
    {
        Resource,
        PassField
    }

    [Serializable]
    public sealed class RenderGraphPassResourceBinding
    {
        public string FieldName;
        public RenderGraphResourceKind ResourceKind;
        public int ResourceIndex;
        public RenderGraphPassBindingSourceKind SourceKind;
        public int SourcePassIndex = -1;
        public string SourceFieldName;
    }

    [Serializable]
    public sealed class RenderGraphPassDefinition
    {
        public string PassType;
        public List<RenderGraphPassResourceBinding> ResourceBindings = new();
    }

    public sealed class RenderGraphData : ScriptableObject
    {
        public long ImportVersion;

        public List<RenderGraphTextureDesc> TextureDescriptors = new();
        public List<RenderGraphBufferDesc> BufferDescriptors = new();

        public List<RenderGraphPassDefinition> Passes = new();
    }
}
