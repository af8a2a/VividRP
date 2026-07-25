using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VividRP.Runtime
{
    /// <summary>
    /// RenderGraph-independent descriptor used to allocate persistent history buffers.
    /// </summary>
    public readonly struct CameraHistoryBufferDescriptor : IEquatable<CameraHistoryBufferDescriptor>
    {
        public CameraHistoryBufferDescriptor(
            int count,
            int stride,
            GraphicsBuffer.Target target = GraphicsBuffer.Target.Structured,
            GraphicsBuffer.UsageFlags usageFlags = GraphicsBuffer.UsageFlags.None)
        {
            Count = Mathf.Max(1, count);
            Stride = Mathf.Max(1, stride);
            Target = target == 0 ? GraphicsBuffer.Target.Structured : target;
            UsageFlags = usageFlags;
        }

        public int Count { get; }
        public int Stride { get; }
        public GraphicsBuffer.Target Target { get; }
        public GraphicsBuffer.UsageFlags UsageFlags { get; }

        public bool Equals(CameraHistoryBufferDescriptor other)
        {
            return Count == other.Count
                && Stride == other.Stride
                && Target == other.Target
                && UsageFlags == other.UsageFlags;
        }

        public override bool Equals(object obj)
        {
            return obj is CameraHistoryBufferDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Count, Stride, Target, UsageFlags);
        }

        public static bool operator ==(
            CameraHistoryBufferDescriptor left,
            CameraHistoryBufferDescriptor right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            CameraHistoryBufferDescriptor left,
            CameraHistoryBufferDescriptor right)
        {
            return !left.Equals(right);
        }
    }

    public delegate GraphicsBuffer CameraHistoryBufferAllocator(
        in CameraHistoryBufferDescriptor descriptor,
        string resourceName,
        int resourceIndex);

    /// <summary>
    /// A camera-relative persistent buffer ring. Frame age 0 is the current write target,
    /// age 1 is the previous completed frame, and so on.
    /// </summary>
    public sealed class CameraHistoryBuffer
    {
        private readonly CameraHistory m_Owner;
        private readonly CameraHistoryId m_Id;
        private readonly int m_FrameCount;
        private readonly CameraHistoryBufferDescriptor m_Descriptor;
        private readonly CameraHistoryBufferAllocator m_Allocator;
        private readonly GraphicsBuffer[] m_Buffers;
        private int m_CurrentIndex;
        private int m_ValidHistoryCount;
        private long m_LastCommittedSequence = -1;
        private bool m_PendingWrite;
        private bool m_Disposed;

        internal CameraHistoryBuffer(
            CameraHistory owner,
            CameraHistoryId id,
            int frameCount,
            in CameraHistoryBufferDescriptor descriptor,
            CameraHistoryBufferAllocator allocator)
        {
            m_Owner = owner;
            m_Id = id;
            m_FrameCount = Mathf.Max(1, frameCount);
            m_Descriptor = descriptor;
            m_Allocator = allocator;
            m_Buffers = new GraphicsBuffer[m_FrameCount];

            var resolvedAllocator = allocator ?? AllocateDefault;
            try
            {
                for (var resourceIndex = 0; resourceIndex < m_Buffers.Length; resourceIndex++)
                {
                    var buffer = resolvedAllocator(
                        descriptor,
                        BuildResourceName(resourceIndex),
                        resourceIndex);
                    if (buffer == null)
                    {
                        throw new InvalidOperationException(
                            $"Camera history buffer allocator returned null for resource index {resourceIndex}.");
                    }

                    m_Buffers[resourceIndex] = buffer;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public CameraHistoryId Id => m_Id;

        public int FrameCount => m_FrameCount;

        public CameraHistoryBufferDescriptor Descriptor => m_Descriptor;

        public GraphicsBuffer GetCurrent()
        {
            return GetFrame(0);
        }

        public GraphicsBuffer GetPrevious()
        {
            return GetFrame(1);
        }

        public GraphicsBuffer GetFrame(int frameAge)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(CameraHistoryBuffer));
            if (frameAge < 0 || frameAge >= m_FrameCount)
                throw new ArgumentOutOfRangeException(nameof(frameAge));

            var frameIndex = m_CurrentIndex - frameAge;
            if (frameIndex < 0)
                frameIndex += m_FrameCount;

            return m_Buffers[frameIndex];
        }

        public bool IsValid(int frameAge = 1)
        {
            if (m_Disposed || frameAge < 0 || frameAge >= m_FrameCount)
                return false;
            if (m_LastCommittedSequence != m_Owner.CurrentSequence - 1)
                return false;

            return frameAge == 0
                ? m_FrameCount == 1 && m_ValidHistoryCount > 0
                : frameAge <= m_ValidHistoryCount;
        }

        /// <summary>
        /// Marks the current buffer as written. It is promoted to history only when the
        /// owning camera frame is committed successfully.
        /// </summary>
        public void MarkWritten()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(CameraHistoryBuffer));
            if (!m_Owner.IsFrameActive)
                throw new InvalidOperationException("Camera history writes must occur inside an active camera frame.");

            m_PendingWrite = true;
        }

        internal bool Matches(
            int frameCount,
            in CameraHistoryBufferDescriptor descriptor,
            CameraHistoryBufferAllocator allocator)
        {
            return m_FrameCount == Mathf.Max(1, frameCount)
                && m_Descriptor.Equals(descriptor)
                && Equals(m_Allocator, allocator);
        }

        internal void BeginFrame()
        {
            m_PendingWrite = false;
        }

        internal void CommitFrame(long sequence)
        {
            if (!m_PendingWrite)
                return;

            m_CurrentIndex = (m_CurrentIndex + 1) % m_FrameCount;
            m_ValidHistoryCount = Mathf.Min(m_ValidHistoryCount + 1, Mathf.Max(1, m_FrameCount - 1));
            if (m_FrameCount == 1)
                m_ValidHistoryCount = 1;
            m_LastCommittedSequence = sequence;
            m_PendingWrite = false;
        }

        internal void AbortFrame()
        {
            m_PendingWrite = false;
        }

        internal void Dispose()
        {
            if (m_Disposed)
                return;

            for (var i = 0; i < m_Buffers.Length; i++)
            {
                m_Buffers[i]?.Dispose();
                m_Buffers[i] = null;
            }

            m_Disposed = true;
        }

        private string BuildResourceName(int resourceIndex)
        {
            var usageName = string.IsNullOrEmpty(m_Id.Name) ? $"History{m_Id.Value}" : m_Id.Name;
            return $"{usageName}_{m_Owner.CameraName}_{resourceIndex}";
        }

        private static GraphicsBuffer AllocateDefault(
            in CameraHistoryBufferDescriptor descriptor,
            string resourceName,
            int resourceIndex)
        {
            return new GraphicsBuffer(
                descriptor.Target,
                descriptor.UsageFlags,
                descriptor.Count,
                descriptor.Stride)
            {
                name = resourceName,
            };
        }
    }
}
