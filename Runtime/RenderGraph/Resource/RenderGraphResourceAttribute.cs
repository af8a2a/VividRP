using System;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public enum RenderGraphResourceBindingMode
    {
        External,
        PassOwnedOverrideable,

        /// <summary>
        /// Legacy hidden non-transient resource path. Use <see cref="TransientResourceAttribute"/> for pass-local scratch resources.
        /// </summary>
        [Obsolete("Use [TransientResource] for pass-local scratch resources. PassOwnedHidden is retained only for legacy non-transient internal resources.", false)]
        PassOwnedHidden
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
        /// For raster passes: marks this texture as the depth attachment.
        /// </summary>
        public bool IsDepthAttachment;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TransientResourceAttribute : Attribute
    {
    }
}
