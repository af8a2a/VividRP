using System;
using Unity.Collections;
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
        /// Creates a runtime copy of this descriptor.
        /// </summary>
        public RenderGraphBufferDesc Clone()
        {
            return (RenderGraphBufferDesc)MemberwiseClone();
        }

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
        private GraphicsBuffer m_ImportedGraphicsBuffer;
        private bool m_OwnsImportedGraphicsBuffer;
        internal BufferHandle innerHandle;

        public RenderGraphBuffer()
        {
            desc = new RenderGraphBufferDesc();
            innerHandle = BufferHandle.nullHandle;
        }

        public static RenderGraphBuffer CreateStructured(string name, int stride)
        {
            return CreateStructured(name, 1, stride);
        }

        public static RenderGraphBuffer CreateStructured(
            string name,
            int count,
            int stride,
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured)
        {
            var descriptor = RenderGraphBufferDesc.CreateStructured(count, stride);
            descriptor.Name = name;
            descriptor.Target = target;

            return new RenderGraphBuffer
            {
                desc = descriptor
            };
        }

        internal GraphicsBuffer ImportedGraphicsBuffer => m_ImportedGraphicsBuffer;

        internal bool HasImportedBuffer => m_ImportedGraphicsBuffer != null;

        internal void SetImportedBuffer(GraphicsBuffer graphicsBuffer)
        {
            if (!ReferenceEquals(m_ImportedGraphicsBuffer, graphicsBuffer))
                ReleaseOwnedImportedBuffer();

            m_ImportedGraphicsBuffer = graphicsBuffer;
            m_OwnsImportedGraphicsBuffer = false;
            innerHandle = default;
        }

        internal void ClearImportedBuffer()
        {
            ReleaseOwnedImportedBuffer();
            m_ImportedGraphicsBuffer = null;
            m_OwnsImportedGraphicsBuffer = false;
            innerHandle = default;
        }

        internal GraphicsBuffer EnsureImportedBuffer()
        {
            if (desc == null)
                return null;

            var requiredCount = Mathf.Max(1, desc.Count);
            var requiredStride = Mathf.Max(1, desc.Stride);
            var requiredTarget = desc.Target;

            if (m_ImportedGraphicsBuffer == null
                || m_ImportedGraphicsBuffer.count < requiredCount
                || m_ImportedGraphicsBuffer.stride != requiredStride)
            {
                ReleaseOwnedImportedBuffer();
                m_ImportedGraphicsBuffer = new GraphicsBuffer(requiredTarget, requiredCount, requiredStride);
                m_OwnsImportedGraphicsBuffer = true;
                innerHandle = default;
            }

            return m_ImportedGraphicsBuffer;
        }

        internal void SetData(Array data)
        {
            EnsureImportedBuffer()?.SetData(data);
        }

        internal void SetData(Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count)
        {
            EnsureImportedBuffer()?.SetData(data, managedBufferStartIndex, graphicsBufferStartIndex, count);
        }

        internal void SetData<T>(NativeArray<T> data) where T : struct
        {
            EnsureImportedBuffer()?.SetData(data);
        }

        internal void SetData<T>(
            NativeArray<T> data,
            int nativeBufferStartIndex,
            int graphicsBufferStartIndex,
            int count) where T : struct
        {
            EnsureImportedBuffer()?.SetData(data, nativeBufferStartIndex, graphicsBufferStartIndex, count);
        }

        private void ReleaseOwnedImportedBuffer()
        {
            if (!m_OwnsImportedGraphicsBuffer)
                return;

            m_ImportedGraphicsBuffer?.Dispose();
            m_ImportedGraphicsBuffer = null;
            m_OwnsImportedGraphicsBuffer = false;
        }

        public bool IsValid() => innerHandle.IsValid();

        public static implicit operator BufferHandle(RenderGraphBuffer buffer)
        {
            return buffer.innerHandle;
        }
        
        public static implicit operator GraphicsBuffer(RenderGraphBuffer buffer)
        {
            return buffer.innerHandle;
        }

    }
}
