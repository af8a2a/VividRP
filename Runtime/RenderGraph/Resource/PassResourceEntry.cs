using System;
using System.Reflection;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public enum PassResourceType
    {
        Texture,
        Buffer,
        RenderList,
        AccelerationStructure
    }

    /// <summary>
    /// A single resource entry collected from a pass via reflection.
    /// Pairs the field metadata with its descriptor and access flags.
    /// </summary>
    public class PassResourceEntry
    {
        /// <summary>
        /// The reflected field on the pass class.
        /// </summary>
        public FieldInfo Field;

        /// <summary>
        /// Display name for this resource. Falls back to field name if not specified.
        /// </summary>
        public string Name;

        /// <summary>
        /// How the pass accesses this resource.
        /// </summary>
        public AccessFlags Access;

        /// <summary>
        /// The type of resource (Texture, Buffer, AccelerationStructure).
        /// </summary>
        public PassResourceType ResourceType;

        /// <summary>
        /// The serializable descriptor instance read from the field.
        /// Cast to RenderGraphTexture / RenderGraphBuffer / RenderGraphRenderList.
        /// </summary>
        public object Descriptor;

        /// <summary>
        /// For raster passes: color attachment index (0-7). -1 means not an attachment.
        /// </summary>
        public int AttachmentIndex = -1;

        /// <summary>
        /// For raster passes: true if this is the depth attachment.
        /// </summary>
        public bool IsDepthAttachment;

        public RenderGraphTexture Texture => Descriptor as RenderGraphTexture;
        public RenderGraphBuffer Buffer => Descriptor as RenderGraphBuffer;
        public RenderGraphRenderList RenderList => Descriptor as RenderGraphRenderList;
    }
}
