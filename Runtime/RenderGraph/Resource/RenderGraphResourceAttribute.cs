using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public enum RenderGraphResourceBindingMode
    {
        External,
        PassOwnedOverrideable
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RenderGraphResource : Attribute
    {
        public string Name;
        public AccessFlags Access;
        public RenderGraphResourceBindingMode BindingMode;

        /// <summary>
        /// For raster passes: color attachment index (0-7). -1 means not an attachment.
        /// </summary>
        public int AttachmentIndex = -1;

        /// <summary>
        /// For raster passes: input attachment index used by framebuffer fetch macros. -1 means not an input attachment.
        /// </summary>
        public int InputAttachmentIndex = -1;

        /// <summary>
        /// For raster passes: marks this texture as the depth attachment.
        /// </summary>
        public bool IsDepthAttachment;

        /// <summary>
        /// Allows a write-only resource to expose an input port for attachment target binding.
        /// </summary>
        public bool AllowWriteOnlyInput;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PassBypassAttribute : Attribute
    {
        public PassBypassAttribute(string sourceFieldName)
        {
            SourceFieldName = sourceFieldName;
        }

        public string SourceFieldName { get; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TransientResourceAttribute : Attribute
    {
    }

    /// <summary>
    /// Declares a pass's access to a named same-frame blackboard slot in <see cref="FrameResourceTable"/>.
    /// Used by the RenderGraph compiler, pass culling, and editor validation to track slot dependencies
    /// without requiring explicit point-to-point .vrdg wires.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class FrameResourceSlotAttribute : Attribute
    {
        public FrameResourceSlotAttribute(string slotName, AccessFlags access)
        {
            SlotName = slotName;
            Access = access;
        }

        public string SlotName { get; }

        public AccessFlags Access { get; }

        public bool Optional { get; set; }
    }

    /// <summary>
    /// Declares a pass's cross-frame access to a named <see cref="CameraHistory"/> resource.
    /// Passes declaring write access are automatically treated as live graph roots during static culling.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class CameraHistoryAccessAttribute : Attribute
    {
        public CameraHistoryAccessAttribute(string historyName, AccessFlags access)
        {
            HistoryName = historyName;
            Access = access;
        }

        public string HistoryName { get; }

        public AccessFlags Access { get; }
    }
}
