using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering
{
    public static class UnsafeGraphContextExtension
    {
        public static CommandBuffer GetNativeCommandBuffer(this UnsafeGraphContext unsafeGraphContext)
        {
            return CommandBufferHelpers.GetNativeCommandBuffer(unsafeGraphContext.cmd);
        }
    }
}