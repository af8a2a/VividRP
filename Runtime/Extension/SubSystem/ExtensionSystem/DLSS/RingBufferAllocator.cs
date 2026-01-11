//------------------------------------------------------------------------------
// RingBufferAllocator.cs - Memory allocator for C# to C++ data passing
//------------------------------------------------------------------------------
// Based on UnityDenoiserPlugin pattern for passing data to native plugin
// via IssuePluginEventAndData.
//------------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DLSS
{
    /// <summary>
    /// Ring buffer allocator for passing structured data from C# to native plugin.
    /// Uses GCHandle.Pinned to provide stable memory addresses that the native
    /// plugin can safely access during render events.
    /// </summary>
    public class RingBufferAllocator : IDisposable
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private int _writePosition;
        private readonly GCHandle _gcHandle;
        private readonly IntPtr _bufferPtr;
        private bool _disposed = false;

        /// <summary>
        /// Creates a new ring buffer allocator with the specified capacity.
        /// </summary>
        /// <param name="capacity">Buffer size in bytes (default: 2MB)</param>
        public RingBufferAllocator(int capacity = 2 * 1024 * 1024)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

            _capacity = capacity;
            _buffer = new byte[capacity];
            _writePosition = 0;

            // Pin the buffer so the native side can access it safely
            _gcHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            _bufferPtr = _gcHandle.AddrOfPinnedObject();
        }

        /// <summary>
        /// Allocates space in the ring buffer for the given struct and returns a native pointer.
        /// </summary>
        /// <typeparam name="T">The type of the struct to store. Must be a blittable struct.</typeparam>
        /// <param name="item">The struct instance to store.</param>
        /// <returns>A native pointer to the location in the ring buffer, or IntPtr.Zero on failure.</returns>
        public IntPtr Allocate<T>(T item) where T : struct
        {
            if (_disposed)
            {
                Debug.LogError("[RingBufferAllocator] Cannot allocate: already disposed");
                return IntPtr.Zero;
            }

            int dataLength = Marshal.SizeOf(item);

            if (dataLength > _capacity)
            {
                Debug.LogError($"[RingBufferAllocator] Allocation failed: Size ({dataLength} bytes) for {typeof(T).Name} exceeds capacity ({_capacity} bytes)");
                return IntPtr.Zero;
            }

            int allocatedOffset;

            // Check if there's enough space at current position
            if (_writePosition + dataLength <= _capacity)
            {
                allocatedOffset = _writePosition;
            }
            else
            {
                // Wrap to beginning
                _writePosition = 0;
                allocatedOffset = 0;
            }

            // Double-check after potential wrap
            if (allocatedOffset + dataLength > _capacity)
            {
                Debug.LogError("[RingBufferAllocator] Allocation failed: No contiguous block available");
                return IntPtr.Zero;
            }

            // Calculate destination pointer and copy struct data
            IntPtr destPtr = (IntPtr)(_bufferPtr.ToInt64() + allocatedOffset);
            Marshal.StructureToPtr(item, destPtr, false);

            // Update write position
            _writePosition = allocatedOffset + dataLength;

            return destPtr;
        }

        /// <summary>
        /// Allocates a contiguous array of elements and returns a pointer for user to fill.
        /// </summary>
        /// <typeparam name="T">The type of elements to allocate</typeparam>
        /// <param name="count">The number of elements to allocate</param>
        /// <returns>Pointer to the allocated memory, or IntPtr.Zero on failure</returns>
        public IntPtr AllocateArray<T>(int count) where T : unmanaged
        {
            if (_disposed)
            {
                Debug.LogError("[RingBufferAllocator] Cannot allocate: already disposed");
                return IntPtr.Zero;
            }

            if (count <= 0)
            {
                Debug.LogError("[RingBufferAllocator] Invalid count: must be positive");
                return IntPtr.Zero;
            }

            int elementSize = Marshal.SizeOf<T>();
            int totalSize = elementSize * count;

            if (totalSize > _capacity)
            {
                Debug.LogError($"[RingBufferAllocator] Allocation failed: Size ({totalSize} bytes) for {count} x {typeof(T).Name} exceeds capacity ({_capacity} bytes)");
                return IntPtr.Zero;
            }

            int allocatedOffset;

            if (_writePosition + totalSize <= _capacity)
            {
                allocatedOffset = _writePosition;
            }
            else
            {
                _writePosition = 0;
                allocatedOffset = 0;
            }

            if (allocatedOffset + totalSize > _capacity)
            {
                Debug.LogError("[RingBufferAllocator] Allocation failed: No contiguous block available");
                return IntPtr.Zero;
            }

            _writePosition = allocatedOffset + totalSize;

            return (IntPtr)(_bufferPtr.ToInt64() + allocatedOffset);
        }

        /// <summary>
        /// Gets the native pointer to the beginning of the pinned buffer.
        /// </summary>
        public IntPtr BufferPointer => _bufferPtr;

        /// <summary>
        /// Gets the total capacity of the ring buffer in bytes.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Resets the write position to the beginning of the buffer.
        /// Call this at the start of each frame to reuse the buffer.
        /// </summary>
        public void Reset()
        {
            _writePosition = 0;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (_gcHandle.IsAllocated)
            {
                _gcHandle.Free();
            }

            _disposed = true;
        }

        ~RingBufferAllocator()
        {
            Dispose(false);
        }
    }
}
