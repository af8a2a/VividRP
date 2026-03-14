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
        /// For raster passes: marks this texture as the depth attachment.
        /// </summary>
        public bool IsDepthAttachment;
    }
}
