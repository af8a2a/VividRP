using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime.RenderGraph.Data
{
    [Serializable]
    public struct AttachmentBinding
    {
        public string FieldName;
        public string DisplayName;
        public string InputPortId;
        public string OutputPortId;
        public ResourceIntent Intent;
        public int MrtIndex;
        public AccessFlags Access;
    }

    [Serializable]
    public struct DepthAttachmentBinding
    {
        public bool IsDefined;
        public string FieldName;
        public string DisplayName;
        public string InputPortId;
        public string OutputPortId;
        public ResourceIntent Intent;
        public AccessFlags Access;
    }

    [Serializable]
    public struct ReadResourceBinding
    {
        public string FieldName;
        public string DisplayName;
        public PortType PortType;
        public string InputPortId;
        public AccessFlags Access;
    }

    [Serializable]
    public struct RendererListBinding
    {
        public string FieldName;
        public string DisplayName;
        public string InputPortId;
    }

    [Serializable]
    public class BakedRasterPass
    {
        public string PassName;
        public string PassLogicTypeName;
        public AttachmentBinding[] ColorAttachments = Array.Empty<AttachmentBinding>();
        public DepthAttachmentBinding DepthAttachment;
        public ReadResourceBinding[] ReadResources = Array.Empty<ReadResourceBinding>();
        public RendererListBinding[] RendererLists = Array.Empty<RendererListBinding>();
        public int[] InputResourceIndices = Array.Empty<int>();
    }
}
