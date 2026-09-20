using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Profiling;

namespace VividRP.Runtime
{
    internal readonly struct VTIOReadCommand
    {
        internal VTIOReadCommand(long fileOffset, int byteSize, bool highPriority)
        {
            if (fileOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(fileOffset));
            if (byteSize < 0)
                throw new ArgumentOutOfRangeException(nameof(byteSize));

            FileOffset = fileOffset;
            ByteSize = byteSize;
            HighPriority = highPriority;
        }

        internal long FileOffset { get; }

        internal int ByteSize { get; }

        internal bool HighPriority { get; }
    }

    internal interface IVTIOBackend : IDisposable
    {
        string Name { get; }

        bool IsAvailable { get; }

        IVTIOBatch CreateBatch(string path, IReadOnlyList<VTIOReadCommand> commands);
    }

    internal interface IVTIOBatch : IDisposable
    {
        int Count { get; }

        bool IsCompleted { get; }

        bool Failed { get; }

        string Error { get; }

        bool TryGetResult(int commandIndex, out byte[] data);

        void Cancel();
    }

    // The returned view is valid only until the batch is disposed.
    internal interface IVTNativeIOBatch : IVTIOBatch
    {
        bool TryGetNativeResult(int commandIndex, out NativeArray<byte> data);
    }

    internal sealed unsafe class VTAsyncReadManagerBackend : IVTIOBackend
    {
        private static readonly ProfilerMarker s_GrowReadBufferMarker = new("VividVT.StreamIO.GrowReadBuffer");
        private static readonly ProfilerMarker s_CreateReadCommandsMarker = new("VividVT.StreamIO.CreateReadCommands");
        private readonly Dictionary<string, SharedFile> m_Files = new(StringComparer.OrdinalIgnoreCase);
        private Batch m_FreeBatch;
        private int m_BatchCount;
        private int m_PreparedBatchCount;
        private int m_PreparedByteCapacity;
        private bool m_Disposed;

        private sealed class SharedFile
        {
            internal FileHandle Handle;
            internal int ReferenceCount;
        }

        internal VTAsyncReadManagerBackend()
        {
            // At most one batch per in-flight chunk under the default streaming budget.
            for (int index = 0; index < VTVirtualTextureStreamRequestGate.DefaultMaxPendingReadCount; index++)
            {
                ReturnBatch(new Batch(this));
                m_BatchCount++;
            }
        }

        internal void Prepare(int batchCount, int byteCapacity)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(VTAsyncReadManagerBackend));
            if (byteCapacity <= 0 || batchCount <= 0
                || (batchCount <= m_PreparedBatchCount && byteCapacity <= m_PreparedByteCapacity))
                return;

            int targetBatchCount = Math.Max(m_PreparedBatchCount, batchCount);
            int targetByteCapacity = Math.Max(m_PreparedByteCapacity, byteCapacity);
            while (m_BatchCount < targetBatchCount)
            {
                ReturnBatch(new Batch(this));
                m_BatchCount++;
            }
            // Active reads/decode views must keep their storage until their batch is returned.
            int preparedCount = 0;
            for (Batch batch = m_FreeBatch; batch != null && preparedCount < targetBatchCount; batch = batch.NextFree)
            {
                batch.EnsureCapacity(targetByteCapacity);
                preparedCount++;
            }
            m_PreparedBatchCount = targetBatchCount;
            m_PreparedByteCapacity = targetByteCapacity;
        }

        public string Name => nameof(VividVirtualTextureIOBackendMode.AsyncReadManager);

        public bool IsAvailable => true;

        public IVTIOBatch CreateBatch(string path, IReadOnlyList<VTIOReadCommand> commands)
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(VTAsyncReadManagerBackend));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("VT stream path must be non-empty.", nameof(path));
            if (commands == null || commands.Count == 0 || commands.Count > 64)
                throw new ArgumentOutOfRangeException(nameof(commands));

            int totalByteSize = 0;
            for (int index = 0; index < commands.Count; index++)
                totalByteSize = checked(totalByteSize + commands[index].ByteSize);

            SharedFile file = AcquireFile(path);
            Batch batch = RentBatch(Math.Max(1, totalByteSize));
            try
            {
                batch.Initialize(path, file, commands, totalByteSize);
                return batch;
            }
            catch
            {
                ReleaseFile(path, file);
                ReturnBatch(batch);
                throw;
            }
        }

        private Batch RentBatch(int requiredCapacity)
        {
            // Prefer the smallest fitting buffer; otherwise grow the largest free one.
            // Return order must not force a smaller buffer to grow while a suitable one is idle.
            Batch best = null;
            Batch bestPrevious = null;
            Batch previous = null;
            for (Batch candidate = m_FreeBatch; candidate != null; candidate = candidate.NextFree)
            {
                int capacity = candidate.BufferCapacity;
                if (best == null
                    || (capacity >= requiredCapacity
                        ? best.BufferCapacity < requiredCapacity || capacity < best.BufferCapacity
                        : best.BufferCapacity < requiredCapacity && capacity > best.BufferCapacity))
                {
                    best = candidate;
                    bestPrevious = previous;
                    if (capacity == requiredCapacity)
                        break;
                }
                previous = candidate;
            }
            if (best == null)
            {
                m_BatchCount++;
                return new Batch(this);
            }
            if (bestPrevious == null)
                m_FreeBatch = best.NextFree;
            else
                bestPrevious.NextFree = best.NextFree;
            best.NextFree = null;
            return best;
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;

            foreach (SharedFile file in m_Files.Values)
                CloseFile(file);
            m_Files.Clear();
            while (m_FreeBatch != null)
            {
                Batch batch = m_FreeBatch;
                m_FreeBatch = batch.NextFree;
                batch.NextFree = null;
                batch.ReleaseBuffers();
            }
            m_Disposed = true;
        }

        private void ReturnBatch(Batch batch)
        {
            if (!m_Disposed)
            {
                batch.NextFree = m_FreeBatch;
                m_FreeBatch = batch;
            }
            else
                batch.ReleaseBuffers();
        }

        private SharedFile AcquireFile(string path)
        {
            if (m_Files.TryGetValue(path, out SharedFile file))
            {
                file.ReferenceCount += 1;
                return file;
            }

            file = new SharedFile
            {
                Handle = AsyncReadManager.OpenFileAsync(path),
                ReferenceCount = 1,
            };
            m_Files.Add(path, file);
            return file;
        }

        private void ReleaseFile(string path, SharedFile file)
        {
            if (file == null || file.ReferenceCount <= 0)
                return;

            file.ReferenceCount -= 1;
            if (file.ReferenceCount == 0 && file.Handle.Status == FileStatus.OpenFailed && !m_Disposed)
            {
                CloseFile(file);
                m_Files.Remove(path);
            }
        }

        private static void CloseFile(SharedFile file)
        {
            if (file == null || !file.Handle.IsValid())
                return;

            if (file.Handle.Status == FileStatus.Pending)
                file.Handle.JobHandle.Complete();
            if (file.Handle.Status != FileStatus.Closed)
                file.Handle.Close(default).Complete();
        }

        private sealed unsafe class Batch : IVTNativeIOBatch
        {
            internal Batch NextFree;
            private readonly int[] m_BufferOffsets = new int[64];
            private readonly int[] m_ByteSizes = new int[64];
            private readonly VTAsyncReadManagerBackend m_Owner;
            private string m_Path;
            private SharedFile m_File;
            private NativeArray<byte> m_Buffer;
            private NativeArray<ReadCommand> m_ReadCommands;
            private ReadHandle m_ReadHandle;
            private bool m_HasReadHandle;
            private bool m_Disposed = true;

            internal Batch(VTAsyncReadManagerBackend owner)
            {
                m_Owner = owner;
            }

            internal void Initialize(
                string path,
                SharedFile file,
                IReadOnlyList<VTIOReadCommand> commands,
                int totalByteSize)
            {
                m_Path = path;
                m_File = file;

                Count = commands.Count;
                m_Disposed = false;
                m_HasReadHandle = false;
                m_ReadHandle = default;
                try
                {
                    int byteOffset = 0;
                    for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                    {
                        m_BufferOffsets[commandIndex] = byteOffset;
                        m_ByteSizes[commandIndex] = commands[commandIndex].ByteSize;
                        byteOffset += commands[commandIndex].ByteSize;
                    }

                    EnsureCapacity(Math.Max(1, totalByteSize));
                    byte* buffer = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(m_Buffer);
                    for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                    {
                        VTIOReadCommand command = commands[commandIndex];
                        m_ReadCommands[commandIndex] = new ReadCommand
                        {
                            Buffer = buffer + m_BufferOffsets[commandIndex],
                            Offset = command.FileOffset,
                            Size = command.ByteSize,
                        };
                    }
                }
                catch
                {
                    ReleaseBuffers();
                    m_File = null;
                    m_Path = null;
                    m_Disposed = true;
                    throw;
                }
            }

            public int Count { get; private set; }

            internal int BufferCapacity => m_Buffer.IsCreated ? m_Buffer.Length : 0;

            internal void EnsureCapacity(int requiredCapacity)
            {
                if (!m_Buffer.IsCreated || m_Buffer.Length < requiredCapacity)
                {
                    using var marker = s_GrowReadBufferMarker.Auto();
                    int capacity = (int)Math.Max(requiredCapacity, Math.Min(int.MaxValue, (long)m_Buffer.Length * 2));
                    if (m_Buffer.IsCreated)
                        m_Buffer.Dispose();
                    m_Buffer = default;
                    m_Buffer = new NativeArray<byte>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
                if (!m_ReadCommands.IsCreated)
                {
                    using var marker = s_CreateReadCommandsMarker.Auto();
                    m_ReadCommands = new NativeArray<ReadCommand>(
                        m_BufferOffsets.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
            }

            public bool IsCompleted
            {
                get
                {
                    EnsureSubmitted();
                    return m_Disposed
                           || m_File == null
                           || m_File.Handle.Status == FileStatus.OpenFailed
                           || (m_HasReadHandle && m_ReadHandle.Status != ReadStatus.InProgress);
                }
            }

            public bool Failed
            {
                get
                {
                    EnsureSubmitted();
                    return m_Disposed
                           || m_File == null
                           || m_File.Handle.Status == FileStatus.OpenFailed
                           || (m_HasReadHandle && m_ReadHandle.Status != ReadStatus.InProgress && m_ReadHandle.Status != ReadStatus.Complete);
                }
            }

            public string Error => Failed
                ? m_File != null && m_File.Handle.Status == FileStatus.OpenFailed
                    ? "AsyncReadManager failed to open the VT stream file."
                    : $"AsyncReadManager completed with status {(m_HasReadHandle ? m_ReadHandle.Status : ReadStatus.Failed)}."
                : null;

            public bool TryGetResult(int commandIndex, out byte[] data)
            {
                data = null;
                if (!TryGetNativeResult(commandIndex, out NativeArray<byte> nativeData))
                    return false;

                data = nativeData.ToArray();
                return true;
            }

            public bool TryGetNativeResult(int commandIndex, out NativeArray<byte> data)
            {
                data = default;
                EnsureSubmitted();
                if (m_Disposed
                    || commandIndex < 0
                    || commandIndex >= Count
                    || !m_HasReadHandle
                    || m_ReadHandle.Status != ReadStatus.Complete
                    || m_ReadHandle.GetBytesRead((uint)commandIndex) != m_ByteSizes[commandIndex])
                {
                    return false;
                }

                data = m_Buffer.GetSubArray(m_BufferOffsets[commandIndex], m_ByteSizes[commandIndex]);
                return true;
            }

            public void Cancel()
            {
                if (!m_Disposed && m_HasReadHandle && m_ReadHandle.Status == ReadStatus.InProgress)
                    m_ReadHandle.Cancel();
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                if (m_HasReadHandle && m_ReadHandle.IsValid())
                {
                    if (m_ReadHandle.Status == ReadStatus.InProgress)
                        m_ReadHandle.Cancel();
                    m_ReadHandle.JobHandle.Complete();
                    m_ReadHandle.Dispose();
                }

                m_Owner.ReleaseFile(m_Path, m_File);
                m_ReadHandle = default;
                m_HasReadHandle = false;
                m_Path = null;
                m_File = null;
                m_Disposed = true;
                m_Owner.ReturnBatch(this);
            }

            internal void ReleaseBuffers()
            {
                if (m_ReadCommands.IsCreated)
                    m_ReadCommands.Dispose();
                if (m_Buffer.IsCreated)
                    m_Buffer.Dispose();
                m_Buffer = default;
                m_ReadCommands = default;
            }

            private void EnsureSubmitted()
            {
                if (m_Disposed || m_HasReadHandle || m_File == null)
                    return;
                if (m_File.Handle.Status == FileStatus.Pending)
                    return;
                if (m_File.Handle.Status != FileStatus.Open)
                    return;

                var commands = new ReadCommandArray
                {
                    ReadCommands = (ReadCommand*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(m_ReadCommands),
                    CommandCount = Count,
                };
                m_ReadHandle = AsyncReadManager.Read(in m_File.Handle, commands);
                m_HasReadHandle = true;
            }
        }
    }
}
