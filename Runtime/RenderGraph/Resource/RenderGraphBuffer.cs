using System;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace VividRP.Runtime
{
    /// <summary>
    /// Serializable buffer descriptor for RenderGraph resources.
    /// Mirrors UnityEngine.Rendering.RenderGraphModule.BufferDesc but can be serialized in assets.
    /// </summary>
    [Serializable]
    public class RenderGraphBufferDesc
    {
        public int Count = 1;
        public int Stride = 4;
        public GraphicsBuffer.Target Target = GraphicsBuffer.Target.Structured;
        public string Name = "Buffer";

        /// <summary>
        /// Converts this serializable descriptor to Unity's BufferDesc.
        /// </summary>
        private BufferDesc ToBufferDesc()
        {
            return new BufferDesc(Count, Stride)
            {
                target = Target,
                name = Name
            };
        }

        public static implicit operator BufferDesc(RenderGraphBufferDesc buffer)
        {
            return buffer.ToBufferDesc();
        }

        /// <summary>
        /// Creates a RenderGraphBufferDesc from Unity's BufferDesc.
        /// </summary>
        public static RenderGraphBufferDesc FromBufferDesc(BufferDesc desc)
        {
            return new RenderGraphBufferDesc
            {
                Count = desc.count,
                Stride = desc.stride,
                Target = desc.target,
                Name = desc.name
            };
        }

        /// <summary>
        /// Creates a default descriptor for a structured buffer.
        /// </summary>
        public static RenderGraphBufferDesc CreateStructured(int count, int stride)
        {
            return new RenderGraphBufferDesc
            {
                Count = count,
                Stride = stride,
                Target = GraphicsBuffer.Target.Structured,
                Name = "StructuredBuffer"
            };
        }

        /// <summary>
        /// Creates a default descriptor for an append/consume buffer.
        /// </summary>
        public static RenderGraphBufferDesc CreateAppend(int count, int stride)
        {
            return new RenderGraphBufferDesc
            {
                Count = count,
                Stride = stride,
                Target = GraphicsBuffer.Target.Append,
                Name = "AppendBuffer"
            };
        }

        /// <summary>
        /// Creates a default descriptor for an indirect arguments buffer.
        /// </summary>
        public static RenderGraphBufferDesc CreateIndirectArguments(int count = 5)
        {
            return new RenderGraphBufferDesc
            {
                Count = count,
                Stride = 4,
                Target = GraphicsBuffer.Target.IndirectArguments,
                Name = "IndirectArgsBuffer"
            };
        }
    }

    [Serializable]
    public class RenderGraphBuffer
    {
        public RenderGraphBufferDesc desc;
        internal BufferHandle innerHandle;

        public static implicit operator BufferHandle(RenderGraphBuffer buffer)
        {
            return buffer.innerHandle;
        }
    }
}