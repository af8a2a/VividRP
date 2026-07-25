using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    internal static class CameraHistoryRenderGraphBridge
    {
        internal static CameraHistoryTextureDescriptor CreateDescriptor(RenderGraphTextureDesc descriptor)
        {
            if (descriptor == null)
                return default;

            return new CameraHistoryTextureDescriptor(
                descriptor.Width,
                descriptor.Height,
                descriptor.ColorFormat,
                descriptor.Slices,
                descriptor.Dimension,
                descriptor.DepthBufferBits,
                descriptor.MsaaSamples,
                descriptor.FilterMode,
                descriptor.WrapMode,
                descriptor.AnisoLevel,
                descriptor.MipMapBias,
                descriptor.EnableRandomWrite,
                descriptor.UseMipMap,
                descriptor.AutoGenerateMips,
                descriptor.IsShadowMap,
                descriptor.BindTextureMS,
                descriptor.UseDynamicScale,
                descriptor.UseDynamicScaleExplicit);
        }

        internal static CameraHistoryBufferDescriptor CreateDescriptor(RenderGraphBufferDesc descriptor)
        {
            if (descriptor == null)
                return default;

            return new CameraHistoryBufferDescriptor(
                descriptor.Count,
                descriptor.Stride,
                descriptor.Target);
        }

        internal static TextureHandle Import(CameraHistoryTexture history, int frameAge)
        {
            if (history == null)
                return default;

            return PassRecorder.ImportTextureHandle(history.GetFrame(frameAge));
        }

        internal static TextureHandle ImportForPass(
            IRenderPass pass,
            CameraHistoryTexture history,
            int frameAge,
            AccessFlags access)
        {
            if (history == null)
                return default;

            return PassRecorder.ImportTextureForPass(pass, history.GetFrame(frameAge), access);
        }

        internal static TextureHandle Bind(
            RenderGraphTexture texture,
            CameraHistoryTexture history,
            int frameAge)
        {
            if (texture == null || history == null)
                return default;

            var handle = Import(history, frameAge);
            PassRecorder.BindImportedTexture(texture, handle);
            return handle;
        }

        internal static TextureHandle BindForPass(
            IRenderPass pass,
            RenderGraphTexture texture,
            CameraHistoryTexture history,
            int frameAge,
            AccessFlags access)
        {
            if (texture == null || history == null)
                return default;

            var handle = ImportForPass(pass, history, frameAge, access);
            PassRecorder.BindImportedTexture(texture, handle);
            return handle;
        }

        internal static BufferHandle Import(CameraHistoryBuffer history, int frameAge)
        {
            if (history == null)
                return default;

            return PassRecorder.ImportBufferHandle(history.GetFrame(frameAge));
        }

        internal static BufferHandle ImportForPass(
            IRenderPass pass,
            CameraHistoryBuffer history,
            int frameAge,
            AccessFlags access)
        {
            if (history == null)
                return default;

            return PassRecorder.ImportBufferForPass(pass, history.GetFrame(frameAge), access);
        }

        internal static BufferHandle Bind(
            RenderGraphBuffer buffer,
            CameraHistoryBuffer history,
            int frameAge)
        {
            if (buffer == null || history == null)
                return default;

            var handle = Import(history, frameAge);
            PassRecorder.BindImportedBuffer(buffer, handle);
            return handle;
        }

        internal static BufferHandle BindForPass(
            IRenderPass pass,
            RenderGraphBuffer buffer,
            CameraHistoryBuffer history,
            int frameAge,
            AccessFlags access)
        {
            if (buffer == null || history == null)
                return default;

            var handle = ImportForPass(pass, history, frameAge, access);
            PassRecorder.BindImportedBuffer(buffer, handle);
            return handle;
        }
    }
}
