using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime.RenderGraph.Data;

namespace VividRP.Runtime.RenderGraph.Resource
{
    public struct ResourceSlot
    {
        public ResourceType Type { get; private set; }
        public TextureHandle TextureHandle { get; private set; }
        public BufferHandle BufferHandle { get; private set; }
        public bool IsValid { get; private set; }

        public static ResourceSlot FromTexture(TextureHandle handle)
        {
            return new ResourceSlot
            {
                Type = ResourceType.Texture,
                TextureHandle = handle,
                IsValid = handle.IsValid()
            };
        }

        public static ResourceSlot FromBuffer(BufferHandle handle)
        {
            return new ResourceSlot
            {
                Type = ResourceType.Buffer,
                BufferHandle = handle,
                IsValid = handle.IsValid()
            };
        }
    }
}
