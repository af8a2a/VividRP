using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    public readonly struct ComputePassContext
    {
        private readonly ComputeGraphContext m_RenderGraphContext;
        private readonly ContextContainer m_FrameData;

        public ComputePassContext(
            ComputeGraphContext renderGraphContext,
            ContextContainer frameData)
        {
            m_RenderGraphContext = renderGraphContext;
            m_FrameData = frameData;
        }

        public ComputeGraphContext renderGraphContext => m_RenderGraphContext;
        public ContextContainer frameData => m_FrameData;
        public ComputeCommandBuffer cmd => m_RenderGraphContext.cmd;
        public RenderGraphObjectPool renderGraphPool => m_RenderGraphContext.renderGraphPool;

        public TextureUVOrigin GetTextureUVOrigin(TextureHandle textureHandle)
        {
            return m_RenderGraphContext.GetTextureUVOrigin(textureHandle);
        }

        public bool Contains<T>() where T : ContextItem, new()
        {
            return m_FrameData != null && m_FrameData.Contains<T>();
        }

        public bool TryGet<T>(out T item) where T : ContextItem, new()
        {
            if (Contains<T>())
            {
                item = m_FrameData.Get<T>();
                return true;
            }

            item = default;
            return false;
        }

        public T Get<T>() where T : ContextItem, new()
        {
            return m_FrameData.Get<T>();
        }

        public T GetOrCreate<T>() where T : ContextItem, new()
        {
            return m_FrameData.GetOrCreate<T>();
        }

        public static implicit operator ComputeGraphContext(ComputePassContext context)
        {
            return context.renderGraphContext;
        }
    }

    public readonly struct RasterPassContext
    {
        private readonly RasterGraphContext m_RenderGraphContext;
        private readonly ContextContainer m_FrameData;

        public RasterPassContext(
            RasterGraphContext renderGraphContext,
            ContextContainer frameData)
        {
            m_RenderGraphContext = renderGraphContext;
            m_FrameData = frameData;
        }

        public RasterGraphContext renderGraphContext => m_RenderGraphContext;
        public ContextContainer frameData => m_FrameData;
        public RasterCommandBuffer cmd => m_RenderGraphContext.cmd;
        public RenderGraphObjectPool renderGraphPool => m_RenderGraphContext.renderGraphPool;

        public TextureUVOrigin GetTextureUVOrigin(TextureHandle textureHandle)
        {
            return m_RenderGraphContext.GetTextureUVOrigin(textureHandle);
        }

        public bool Contains<T>() where T : ContextItem, new()
        {
            return m_FrameData != null && m_FrameData.Contains<T>();
        }

        public bool TryGet<T>(out T item) where T : ContextItem, new()
        {
            if (Contains<T>())
            {
                item = m_FrameData.Get<T>();
                return true;
            }

            item = default;
            return false;
        }

        public T Get<T>() where T : ContextItem, new()
        {
            return m_FrameData.Get<T>();
        }

        public T GetOrCreate<T>() where T : ContextItem, new()
        {
            return m_FrameData.GetOrCreate<T>();
        }

        public static implicit operator RasterGraphContext(RasterPassContext context)
        {
            return context.renderGraphContext;
        }
    }

    public readonly struct UnsafePassContext
    {
        private readonly UnsafeGraphContext m_RenderGraphContext;
        private readonly ContextContainer m_FrameData;

        public UnsafePassContext(
            UnsafeGraphContext renderGraphContext,
            ContextContainer frameData)
        {
            m_RenderGraphContext = renderGraphContext;
            m_FrameData = frameData;
        }

        public UnsafeGraphContext renderGraphContext => m_RenderGraphContext;
        public ContextContainer frameData => m_FrameData;
        public UnsafeCommandBuffer cmd => m_RenderGraphContext.cmd;
        public RenderGraphObjectPool renderGraphPool => m_RenderGraphContext.renderGraphPool;

        public TextureUVOrigin GetTextureUVOrigin(TextureHandle textureHandle)
        {
            return m_RenderGraphContext.GetTextureUVOrigin(textureHandle);
        }

        public CommandBuffer GetNativeCommandBuffer()
        {
            return CommandBufferHelpers.GetNativeCommandBuffer(m_RenderGraphContext.cmd);
        }

        public bool Contains<T>() where T : ContextItem, new()
        {
            return m_FrameData != null && m_FrameData.Contains<T>();
        }

        public bool TryGet<T>(out T item) where T : ContextItem, new()
        {
            if (Contains<T>())
            {
                item = m_FrameData.Get<T>();
                return true;
            }

            item = default;
            return false;
        }

        public T Get<T>() where T : ContextItem, new()
        {
            return m_FrameData.Get<T>();
        }

        public T GetOrCreate<T>() where T : ContextItem, new()
        {
            return m_FrameData.GetOrCreate<T>();
        }

        public static implicit operator UnsafeGraphContext(UnsafePassContext context)
        {
            return context.renderGraphContext;
        }
    }
}
