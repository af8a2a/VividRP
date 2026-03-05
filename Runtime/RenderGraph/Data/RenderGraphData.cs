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

    [Serializable]
    public sealed class RenderGraphPassResourceBinding
    {
        public string FieldName;
        public RenderGraphResourceKind ResourceKind;
        public int ResourceIndex;
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
